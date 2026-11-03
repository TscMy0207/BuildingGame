using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>
/// 使用Unity内置池管理预制体实例及其资源引用 每个实例只在创建时注入依赖
/// </summary>
[AutoRegister(typeof(IObjectPoolService), typeof(IStartupService), AsSelf = false)]
internal class ObjectPoolService : ISystem, IObjectPoolService, IStartupService
{
    /// <summary>当前容器的资源加载服务</summary>
    private readonly IResourcesAssetLoader _resources;
    /// <summary>创建实例时使用的容器 不允许跨容器复用</summary>
    private readonly IObjectResolver _resolver;
    /// <summary>生命周期回调异常的日志出口</summary>
    private readonly ILogger<ObjectPoolService> _logger;
    /// <summary>初始化中的池也提前登记 以合并同地址的并发请求</summary>
    private readonly Dictionary<string, PoolEntry> _pools = new(StringComparer.Ordinal);
    /// <summary>借出实例的归属 用于阻止重复回收与跨Scope回收</summary>
    private readonly Dictionary<GameObject, PoolEntry> _rented = new();
    /// <summary>已经关闭但还在等待资源加载或延迟销毁的清理任务</summary>
    private readonly List<PoolEntry> _closing = new();
    /// <summary>服务创建线程 必须为Unity主线程</summary>
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;
    /// <summary>所属框架或模块的生命周期</summary>
    private CancellationToken _lifetimeToken;
    private bool _initialized;
    private bool _disposed;
    private bool _notifying;

    /// <summary>在资源服务之后启动</summary>
    public int Priority => FrameworkSettings.ObjectPoolStartupPriority;
    /// <summary>启动流程显示名称</summary>
    public string Description => "对象池服务";
    /// <summary>启动进度权重</summary>
    public float Weight => FrameworkSettings.ObjectPoolStartupWeight;

    /// <summary>
    /// 绑定当前容器的资源 依赖注入和日志服务
    /// </summary>
    /// <param name="resources">预制体资源引用所有者</param>
    /// <param name="resolver">实例依赖注入容器</param>
    /// <param name="logger">对象池日志</param>
    public ObjectPoolService(IResourcesAssetLoader resources, IObjectResolver resolver, ILogger<ObjectPoolService> logger)
    {
        _resources = resources;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// 记录服务生命周期 池和宿主按首次使用创建
    /// </summary>
    /// <param name="context">启动上下文</param>
    /// <returns>已经完成的初始化任务</returns>
    public UniTask StartAsync(StartupContext context)
    {
        Initialize(context.LifetimeToken);
        context.ReportProgress?.Invoke(1f, "对象池服务初始化完成");
        return UniTask.CompletedTask;
    }

    /// <summary>
    /// 供全局启动及模块Scope使用相同的初始化规则
    /// </summary>
    /// <param name="lifetimeToken">服务所属生命周期</param>
    internal void Initialize(CancellationToken lifetimeToken)
    {
        CheckThreadAndCallback();
        if (_disposed) throw new ObjectDisposedException(nameof(ObjectPoolService));
        if (_initialized) throw new InvalidOperationException("对象池服务不能重复初始化");
        lifetimeToken.ThrowIfCancellationRequested();
        _lifetimeToken = lifetimeToken;
        _initialized = true;
    }

    /// <summary>
    /// 创建真实的不同实例进行预热 不把重复取出同一个对象误算为预热
    /// </summary>
    /// <param name="location">预制体资源地址</param>
    /// <param name="count">期望空闲数量</param>
    /// <param name="cancellationToken">本次等待取消令牌</param>
    /// <returns>预热任务</returns>
    public async UniTask PrewarmAsync(string location, int count, CancellationToken cancellationToken = default)
    {
        Validate(cancellationToken);
        if (count < 0 || count > FrameworkSettings.ObjectPoolMaxInactiveCount)
            throw new ArgumentOutOfRangeException(nameof(count), "预热数量必须在零与对象池缓存上限之间");
        var entry = await GetPoolAsync(location, cancellationToken);
        Validate(cancellationToken);
        EnsureOpen(entry);
        var instances = new List<GameObject>();
        try
        {
            // 先取走空闲实例再创建差额 最后统一归还 避免循环复用同一个实例
            for (var i = 0; i < count; i++) instances.Add(GetLivingInstance(entry));
        }
        finally
        {
            foreach (var instance in instances) entry.Pool.Release(instance);
        }
    }

    /// <summary>
    /// 等待共享资源加载并取出实例 首次激活前已经完成注入
    /// </summary>
    /// <param name="location">预制体资源地址</param>
    /// <param name="parent">本次使用的父节点</param>
    /// <param name="cancellationToken">本次等待取消令牌</param>
    /// <returns>借出的实例</returns>
    public async UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
    {
        Validate(cancellationToken);
        var hadParent = parent != null;
        var entry = await GetPoolAsync(location, cancellationToken);
        Validate(cancellationToken);
        EnsureOpen(entry);
        if (hadParent && parent == null) throw new InvalidOperationException("对象池目标父节点已销毁");
        var instance = GetLivingInstance(entry);
        _rented.Add(instance, entry);
        // 激活与通知期间禁止池操作重入 避免OnEnable或回调回收正在取出的实例
        _notifying = true;
        try
        {
            instance.transform.SetParent(parent != null ? parent : entry.ActiveRoot.transform, false);
            ResetTransform(instance, entry.Prefab);
            instance.SetActive(true);
            Notify(entry, instance, true);
        }
        finally { _notifying = false; }
        return instance;
    }

    /// <summary>
    /// 停用并归还本服务借出的实例 缓存已满时由Unity池销毁多余实例
    /// </summary>
    /// <param name="instance">待回收实例</param>
    /// <returns>是否属于当前服务且处于借出状态</returns>
    public bool Despawn(GameObject instance)
    {
        CheckThreadAndCallback();
        if (ReferenceEquals(instance, null) || !_rented.TryGetValue(instance, out var entry)) return false;
        _rented.Remove(instance);
        if (instance == null) { entry.Callbacks.Remove(instance); return false; }
        ReturnInstance(entry, instance);
        return true;
    }

    /// <summary>
    /// 关闭指定池并等待其资源引用释放 关闭期间的新请求可以创建独立新池
    /// </summary>
    /// <param name="location">资源地址</param>
    /// <returns>清理任务</returns>
    public UniTask ClearAsync(string location)
    {
        CheckThreadAndCallback();
        if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("对象池资源地址不能为空", nameof(location));
        if (!_pools.TryGetValue(location, out var entry))
        {
            var pending = new List<UniTask>();
            foreach (var closing in _closing)
                if (closing.Location == location) pending.Add(closing.Cleared.Task);
            return UniTask.WhenAll(pending);
        }
        _pools.Remove(location);
        return Close(entry);
    }

    /// <summary>
    /// 关闭所有已登记的池 并等待此前启动的延迟清理
    /// </summary>
    /// <returns>全部资源清理任务</returns>
    public UniTask ClearAllAsync()
    {
        CheckThreadAndCallback();
        foreach (var entry in new List<PoolEntry>(_pools.Values))
        {
            _pools.Remove(entry.Location);
            Close(entry);
        }
        var pending = new List<UniTask>();
        foreach (var entry in _closing) pending.Add(entry.Cleared.Task);
        return UniTask.WhenAll(pending);
    }

    /// <summary>
    /// 立即停用全部实例 并启动等待Unity销毁完成的资源清理 可重复调用
    /// </summary>
    public void Dispose()
    {
        CheckThreadAndCallback();
        if (_disposed) return;
        _disposed = true;
        ClearAllAsync().Forget(exception => _logger.Error("清理对象池资源失败", exception));
    }

    /// <summary>
    /// 获取合并的加载任务 调用方取消不影响其他等待者
    /// </summary>
    /// <param name="location">预制体地址</param>
    /// <param name="cancellationToken">调用方取消令牌</param>
    /// <returns>已加载的池</returns>
    private async UniTask<PoolEntry> GetPoolAsync(string location, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location) || !location.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("对象池地址必须为包名/预制体路径且以.prefab结尾", nameof(location));
        if (!_pools.TryGetValue(location, out var entry))
        {
            entry = new PoolEntry(location);
            _pools.Add(location, entry);
            LoadPoolAsync(entry).Forget();
        }
        // 已加载的池直接返回 避免每次取出都分配链接取消源
        if (entry.Loaded.Task.Status.IsCompleted()) return await entry.Loaded.Task;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        return await entry.Loaded.Task.AttachExternalCancellation(linked.Token);
    }

    /// <summary>
    /// 只为每个池持有一次资源引用 加载失败或加载期间关闭时配对归还
    /// </summary>
    /// <param name="entry">提前登记的池记录</param>
    /// <returns>完成共享加载结果登记的任务</returns>
    private async UniTask LoadPoolAsync(PoolEntry entry)
    {
        var acquired = false;
        try
        {
            var prefab = await _resources.LoadAssetAsync<GameObject>(entry.Location);
            acquired = true;
            if (entry.Closed || _disposed || _lifetimeToken.IsCancellationRequested)
                throw new OperationCanceledException("对象池所属生命周期已结束");
            if (prefab == null) throw new InvalidOperationException($"对象池预制体为空:{entry.Location}");
            entry.Prefab = prefab;
            entry.ActiveRoot = new GameObject($"对象池:{entry.Location}");
            Object.DontDestroyOnLoad(entry.ActiveRoot);
            entry.Storage = new GameObject("空闲对象");
            entry.Storage.transform.SetParent(entry.ActiveRoot.transform, false);
            entry.Storage.SetActive(false);
            entry.Pool = new ObjectPool<GameObject>(() => CreateInstance(entry), actionOnDestroy: instance => DestroyInstance(entry, instance),
                collectionCheck: true, defaultCapacity: 0, maxSize: FrameworkSettings.ObjectPoolMaxInactiveCount);
            entry.Loaded.TrySetResult(entry);
        }
        catch (Exception exception)
        {
            if (_pools.TryGetValue(entry.Location, out var current) && ReferenceEquals(current, entry)) _pools.Remove(entry.Location);
            if (entry.ActiveRoot != null) Object.Destroy(entry.ActiveRoot);
            if (acquired) _resources.ReleaseAsset<GameObject>(entry.Location);
            entry.Loaded.TrySetException(exception);
        }
    }

    /// <summary>
    /// 在不激活实例的父节点下创建对象 完成注入后缓存生命周期回调
    /// </summary>
    /// <param name="entry">所属池</param>
    /// <returns>未激活的实例</returns>
    private GameObject CreateInstance(PoolEntry entry)
    {
        if (entry.Storage == null) throw new InvalidOperationException("对象池宿主已被外部销毁 请清理后重建");
        var instance = Object.Instantiate(entry.Prefab, entry.Storage.transform, false);
        instance.SetActive(false);
        try
        {
            _notifying = true;
            _resolver.InjectGameObject(instance);
            var callbacks = new List<IPooledObject>();
            foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IPooledObject callback) callbacks.Add(callback);
            entry.Callbacks.Add(instance, callbacks.ToArray());
            return instance;
        }
        catch { Object.Destroy(instance); throw; }
        finally { _notifying = false; }
    }

    /// <summary>
    /// 跳过被外部销毁的空闲对象 不向调用方返回失效的Unity引用
    /// </summary>
    /// <param name="entry">所属池</param>
    /// <returns>有效实例</returns>
    private GameObject GetLivingInstance(PoolEntry entry)
    {
        GameObject instance;
        do
        {
            instance = entry.Pool.Get();
            if (instance == null && !ReferenceEquals(instance, null)) entry.Callbacks.Remove(instance);
        } while (instance == null);
        return instance;
    }

    /// <summary>
    /// 触发回收通知并停用对象 然后放入空闲缓存
    /// </summary>
    /// <param name="entry">所属池</param>
    /// <param name="instance">借出实例</param>
    private void ReturnInstance(PoolEntry entry, GameObject instance)
    {
        _notifying = true;
        try
        {
            Notify(entry, instance, false);
            if (instance == null) { entry.Callbacks.Remove(instance); return; }
            instance.SetActive(false);
            instance.transform.SetParent(entry.Storage.transform, false);
            ResetTransform(instance, entry.Prefab);
            entry.Pool.Release(instance);
        }
        finally { _notifying = false; }
    }

    /// <summary>
    /// 启动可被多个调用者等待的关闭任务
    /// </summary>
    /// <param name="entry">待关闭的池</param>
    /// <returns>共享清理任务</returns>
    private UniTask Close(PoolEntry entry)
    {
        entry.Closed = true;
        _closing.Add(entry);
        CloseAndSignalAsync(entry).Forget();
        // 清理任务数量仅与池数有关 在每次新增任务时移除已完成记录
        _closing.RemoveAll(item => item.Cleared.Task.Status == UniTaskStatus.Succeeded);
        return entry.Cleared.Task;
    }

    /// <summary>
    /// 将关闭结果转发给可被并发等待的完成源 避免Dispose与显式清理争用异步任务
    /// </summary>
    /// <param name="entry">待清理的池记录</param>
    /// <returns>完成结果转发任务</returns>
    private async UniTask CloseAndSignalAsync(PoolEntry entry)
    {
        try
        {
            await CloseEntryAsync(entry);
            entry.Cleared.TrySetResult();
        }
        catch (Exception exception) { entry.Cleared.TrySetException(exception); }
    }

    /// <summary>
    /// 销毁包含借出对象在内的全部实例 等待一帧后归还预制体引用
    /// </summary>
    /// <param name="entry">已经标记关闭的池</param>
    /// <returns>清理任务</returns>
    private async UniTask CloseEntryAsync(PoolEntry entry)
    {
        try { await entry.Loaded.Task; }
        catch { return; } // 加载失败的引用已在加载流程中归还
        foreach (var instance in new List<GameObject>(entry.Callbacks.Keys))
        {
            if (_rented.Remove(instance) && instance != null)
            {
                _notifying = true;
                try { Notify(entry, instance, false); }
                finally { _notifying = false; }
            }
            DestroyInstance(entry, instance);
        }
        entry.Pool.Clear();
        if (entry.ActiveRoot != null) Object.Destroy(entry.ActiveRoot);
        await UniTask.NextFrame();
        _resources.ReleaseAsset<GameObject>(entry.Location);
    }

    /// <summary>
    /// 销毁缓存溢出或被清理的实例 并移除其回调记录
    /// </summary>
    /// <param name="entry">所属池</param>
    /// <param name="instance">待销毁实例</param>
    private void DestroyInstance(PoolEntry entry, GameObject instance)
    {
        if (ReferenceEquals(instance, null)) return;
        entry.Callbacks.Remove(instance);
        if (instance == null) return;
        var wasNotifying = _notifying;
        _notifying = true;
        try
        {
            instance.SetActive(false);
            Object.Destroy(instance);
        }
        finally { _notifying = wasNotifying; }
    }

    /// <summary>
    /// 隔离单个业务回调异常 确保其他组件收到通知并完成回收
    /// </summary>
    /// <param name="entry">所属池</param>
    /// <param name="instance">通知对象</param>
    /// <param name="spawning">是否为取出通知</param>
    private void Notify(PoolEntry entry, GameObject instance, bool spawning)
    {
        if (!entry.Callbacks.TryGetValue(instance, out var callbacks)) return;
        foreach (var callback in callbacks)
        {
            if (callback is Object unityObject && unityObject == null) continue;
            try
            {
                if (spawning) callback.OnSpawned();
                else callback.OnDespawned();
            }
            catch (Exception exception) { _logger.Error($"执行对象池{(spawning ? "取出" : "回收")}回调失败:{entry.Location}", exception); }
        }
    }

    /// <summary>
    /// 恢复预制体的局部变换 不推测或重置业务组件状态
    /// </summary>
    /// <param name="instance">池实例</param>
    /// <param name="prefab">原预制体</param>
    private static void ResetTransform(GameObject instance, GameObject prefab)
    {
        instance.transform.localPosition = prefab.transform.localPosition;
        instance.transform.localRotation = prefab.transform.localRotation;
        instance.transform.localScale = prefab.transform.localScale;
    }

    /// <summary>
    /// 检查服务状态及调用方取消
    /// </summary>
    /// <param name="cancellationToken">本次调用的取消令牌</param>
    private void Validate(CancellationToken cancellationToken)
    {
        CheckThreadAndCallback();
        if (_disposed) throw new ObjectDisposedException(nameof(ObjectPoolService));
        if (!_initialized) throw new InvalidOperationException("对象池服务尚未初始化");
        _lifetimeToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 禁止后台线程调用以及生命周期回调内重入池操作
    /// </summary>
    private void CheckThreadAndCallback()
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId) throw new InvalidOperationException("对象池只能在Unity主线程使用");
        if (_notifying) throw new InvalidOperationException("对象池注入或生命周期回调期间不能重入池操作");
    }

    /// <summary>
    /// 防止等待期间被清理的旧池重新生成对象
    /// </summary>
    /// <param name="entry">加载结束的池</param>
    private static void EnsureOpen(PoolEntry entry)
    {
        if (entry.Closed) throw new OperationCanceledException("对象池已被清理");
    }

    /// <summary>
    /// 单个资源地址的原生对象池与资源所有权记录
    /// </summary>
    private sealed class PoolEntry
    {
        /// <summary>池唯一资源地址</summary>
        public readonly string Location;
        /// <summary>可被多个请求等待的初始化结果</summary>
        public readonly UniTaskCompletionSource<PoolEntry> Loaded = new();
        /// <summary>可被Dispose及多次显式清理同时等待的关闭结果</summary>
        public readonly UniTaskCompletionSource Cleared = new();
        /// <summary>所有尚未销毁实例及其创建时收集的回调</summary>
        public readonly Dictionary<GameObject, IPooledObject[]> Callbacks = new();
        /// <summary>池持有的预制体引用</summary>
        public GameObject Prefab;
        /// <summary>活动实例的默认跨场景父节点</summary>
        public GameObject ActiveRoot;
        /// <summary>始终停用的空闲实例父节点</summary>
        public GameObject Storage;
        /// <summary>Unity内置GameObject池</summary>
        public ObjectPool<GameObject> Pool;
        /// <summary>关闭后禁止新请求生成实例</summary>
        public bool Closed;

        /// <summary>
        /// 创建尚未加载资源的池记录
        /// </summary>
        /// <param name="location">预制体地址</param>
        public PoolEntry(string location) => Location = location;
    }
}
