using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// 通过全局容器打开和关闭UI面板
/// </summary>
public interface IUIService
{
    /// <summary>
    /// 加载面板并从全局容器注入依赖 关闭面板或销毁对象时归还资源引用
    /// </summary>
    /// <param name="key">UI键值</param>
    /// <param name="args">打开参数</param>
    /// <param name="cancellationToken">打开期间的取消令牌</param>
    /// <returns>面板句柄</returns>
    UniTask<UIPanelHandle> OpenAsync<TArgs>(UIKey<TArgs> key, TArgs args, CancellationToken cancellationToken = default);
    /// <summary>
    /// 关闭指定面板
    /// </summary>
    UniTask CloseAsync(UIPanelHandle handle);
    /// <summary>
    /// 请求销毁指定面板 资源在Unity完成销毁后自动归还
    /// </summary>
    void DisposeImmediately(UIPanelHandle handle);
}

/// <summary>
/// 持有全局UI根节点并管理已打开面板
/// </summary>
[AutoRegister(typeof(IUIService), typeof(IStartupService), AsSelf = false)]
internal class UIService : ISystem, IStartupService, IUIService
{
    /// <summary>
    /// UI服务的资源加载入口
    /// </summary>
    private readonly IResourcesService _resources;
    /// <summary>
    /// 面板配置来源
    /// </summary>
    private readonly IFrameworkConfigTableService _configTableService;
    /// <summary>
    /// 异常记录器
    /// </summary>
    private readonly ILogger<UIService> _logger;
    /// <summary>
    /// 注册当前单例服务的全局容器
    /// </summary>
    private readonly IObjectResolver _rootResolver;
    /// <summary>
    /// 当前服务拥有的面板实例
    /// </summary>
    private readonly Dictionary<UIPanelHandle, PanelInstance> _instances = new();
    /// <summary>
    /// 服务销毁时取消尚未结束的打开流程
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();
    /// <summary>
    /// 全局UI层级节点
    /// </summary>
    private UIRoot _uiRoot;
    /// <summary>
    /// 启动协调器提供的全局生命周期
    /// </summary>
    private CancellationToken _lifetimeToken = new(true);
    /// <summary>
    /// 下一个实例编号
    /// </summary>
    private int _nextHandleId;
    /// <summary>
    /// 服务是否已关闭
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// 全局UI根预制体地址
    /// </summary>
    private const string RootPrefab = FrameworkSettings.UIRootLocation;

    /// <summary>
    /// 接收框架服务及所属全局容器
    /// </summary>
    public UIService(IResourcesService resources, IFrameworkConfigTableService configTableService,
        ILogger<UIService> logger, IObjectResolver rootResolver)
    {
        _resources = resources;
        _configTableService = configTableService;
        _logger = logger;
        _rootResolver = rootResolver;
    }

    #region 服务生命周期

    /// <summary>
    /// 启动顺序
    /// </summary>
    public int Priority => FrameworkSettings.UIStartupPriority;
    /// <summary>
    /// 启动进度说明
    /// </summary>
    public string Description => "UI服务";
    /// <summary>
    /// 启动进度权重
    /// </summary>
    public float Weight => FrameworkSettings.UIStartupWeight;

    /// <summary>
    /// 加载并保留全局UI根节点
    /// </summary>
    public async UniTask StartAsync(StartupContext context)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UIService));
        _lifetimeToken = context.LifetimeToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken, _shutdown.Token);
        linked.Token.ThrowIfCancellationRequested();
        context.ReportProgress?.Invoke(0.1f, "加载UI根节点");
        if (_uiRoot == null)
        {
            GameObject instance = null;
            try
            {
                instance = await _resources.LoadAndInstantiatePrefabWithResolverAsync(
                    RootPrefab, _rootResolver, cancellationToken: linked.Token);
                _uiRoot = instance.GetComponent<UIRoot>();
                if (_uiRoot == null) throw new InvalidOperationException($"UI根预制体缺少UIRoot脚本:{RootPrefab}");
                Object.DontDestroyOnLoad(instance);
            }
            catch
            {
                if (instance != null) Object.Destroy(instance);
                _uiRoot = null;
                throw;
            }
        }
        linked.Token.ThrowIfCancellationRequested();
        context.ReportProgress?.Invoke(1f, "UI根节点初始化完成");
    }

    /// <summary>
    /// 取消所有打开流程并销毁当前服务拥有的UI对象
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _shutdown.Cancel(); }
        catch (Exception exception) { _logger.Error("取消UI打开流程时清理失败", exception); }
        foreach (var handle in new List<UIPanelHandle>(_instances.Keys)) DisposeImmediately(handle);
        if (_uiRoot != null) Object.Destroy(_uiRoot.gameObject);
        _uiRoot = null;
        _shutdown.Dispose();
    }

    #endregion

    /// <summary>
    /// 使用全局容器创建面板 并处理打开取消或外部销毁
    /// </summary>
    public async UniTask<UIPanelHandle> OpenAsync<TArgs>(UIKey<TArgs> key, TArgs args,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UIService));
        if (key == null) throw new ArgumentNullException(nameof(key));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken, _shutdown.Token, cancellationToken);
        var openingToken = linked.Token;
        openingToken.ThrowIfCancellationRequested();
        if (_uiRoot == null) throw new InvalidOperationException("UI服务尚未初始化");
        if (!_configTableService.TryGetPanelConfig(key.Id, out var config))
            throw new KeyNotFoundException($"未找到UI面板配置{key.Name}");

        GameObject instance = null;
        PanelInstance panel = null;
        try
        {
            instance = await _resources.LoadAndInstantiatePrefabWithResolverAsync(
                config.PrefabLocation, _rootResolver, _uiRoot.GetLayerRoot(config.Layer), false, openingToken);
            if (instance.GetComponent<IUIPanelView>() is not IUIPanelView<TArgs> view)
                throw new InvalidOperationException($"UI面板{key.Name}参数类型不匹配或缺少视图 期望{typeof(TArgs).Name}");
            var handle = new UIPanelHandle(checked(++_nextHandleId));
            panel = new PanelInstance(this, handle, key.Id, instance);
            _instances.Add(handle, panel);
            panel.AddTo(instance);
            using var panelOpening = CancellationTokenSource.CreateLinkedTokenSource(openingToken, panel.DestroyedToken);
            await view.OnOpeningAsync(args, panelOpening.Token);
            panelOpening.Token.ThrowIfCancellationRequested();
            if (instance == null) throw new OperationCanceledException("UI面板在打开期间已销毁");
            return handle;
        }
        catch (Exception exception)
        {
            if (panel != null) DisposeImmediately(panel.Handle);
            else if (instance != null) Object.Destroy(instance);
            if (exception is not OperationCanceledException)
                _logger.Error($"打开UI面板{key.Name}时出现异常", exception);
            throw;
        }
    }

    /// <summary>
    /// 关闭面板并返回已完成任务
    /// </summary>
    public UniTask CloseAsync(UIPanelHandle handle)
    {
        DisposeImmediately(handle);
        return UniTask.CompletedTask;
    }

    /// <summary>
    /// 取消面板打开流程并请求Unity销毁对象
    /// </summary>
    public void DisposeImmediately(UIPanelHandle handle)
    {
        if (!_instances.TryGetValue(handle, out var panel)) return;
        try { panel.Dispose(); }
        catch (Exception exception) { _logger.Error($"关闭UI面板{panel.Id}时清理失败", exception); }
        finally { if (panel.GameObject != null) Object.Destroy(panel.GameObject); }
    }

    /// <summary>
    /// 跟踪单个面板的销毁并移除全局实例记录
    /// </summary>
    private class PanelInstance : LifetimeHandle
    {
        /// <summary>
        /// 拥有该面板的UI服务
        /// </summary>
        private UIService _owner;
        /// <summary>
        /// 取消面板尚未完成的打开任务
        /// </summary>
        private readonly CancellationTokenSource _destroyed = new();
        /// <summary>
        /// 面板配置编号
        /// </summary>
        public int Id { get; }
        /// <summary>
        /// 面板实例编号
        /// </summary>
        public UIPanelHandle Handle { get; }
        /// <summary>
        /// Unity面板对象
        /// </summary>
        public GameObject GameObject { get; }
        /// <summary>
        /// 面板销毁取消令牌
        /// </summary>
        public CancellationToken DestroyedToken { get; }
        /// <summary>
        /// 记录实例所有者并缓存可在释放后读取的令牌
        /// </summary>
        public PanelInstance(UIService owner, UIPanelHandle handle, int id, GameObject gameObject)
        {
            _owner = owner;
            Handle = handle;
            Id = id;
            GameObject = gameObject;
            DestroyedToken = _destroyed.Token;
        }
        /// <summary>
        /// 先移除记录再取消回调以防止清理重入
        /// </summary>
        protected override void OnDispose()
        {
            _owner._instances.Remove(Handle);
            _owner = null;
            try { _destroyed.Cancel(); }
            finally { _destroyed.Dispose(); }
        }
    }
}
