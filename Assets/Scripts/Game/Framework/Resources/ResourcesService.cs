using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;

/// <summary>
/// 资源服务接口
/// 方法约束与IResourcesAssetLoader一致
/// </summary>
public interface IResourcesService : IResourcesAssetLoader
{
    /// <summary>
    /// 启动指定YooAsset场景的异步加载。
    /// 调用方通过返回句柄读取进度，并在allowSceneActivation为false时决定激活时机；
    /// 不再需要场景时必须调用UnloadSceneAsync配对卸载。
    /// </summary>
    /// <param name="location">包名/包内场景路径</param>
    /// <param name="sceneMode">场景加载模式</param>
    /// <param name="physicsMode">场景物理模式</param>
    /// <param name="allowSceneActivation">是否在加载完成后立即激活场景</param>
    /// <param name="priority">YooAsset加载优先级</param>
    /// <returns>正在加载或已加载的场景句柄</returns>
    SceneHandle LoadSceneAsync(
        string location,
        LoadSceneMode sceneMode = LoadSceneMode.Single,
        LocalPhysicsMode physicsMode = LocalPhysicsMode.None,
        bool allowSceneActivation = true,
        uint priority = 0);

    /// <summary>
    /// 卸载由LoadSceneAsync获取的场景句柄
    /// </summary>
    /// <param name="handle">待卸载的场景句柄</param>
    /// <param name="cancellationToken">调用取消令牌</param>
    /// <returns>等待卸载完成的异步任务</returns>
    UniTask UnloadSceneAsync(SceneHandle handle, CancellationToken cancellationToken = default);
}

/// <summary>
/// 资源管理服务
/// </summary>
[AutoRegister(typeof(IResourcesService), typeof(IResourcesAssetLoader), typeof(IStartupService), AsSelf = false)]
internal class ResourcesService : ISystem, IStartupService, IResourcesService
{
    /// <summary>
    /// 移除资源包名前缀 将剩余地址直接交给YooAsset解析
    /// </summary>
    /// <param name="location">已验证的包名/采集地址</param>
    /// <returns>包内采集地址</returns>
    private static string GetAssetLocation(string location) => location.Substring(location.IndexOf('/') + 1);

    private readonly IResourcesPackageManager m_packageManager;
    private readonly ILogger<ResourcesService> m_logger;
    private CancellationToken m_lifetimeToken;

    /// <summary>
    /// 正在加载中的资源 记录加载任务与等待者数量
    /// </summary>
    private readonly Dictionary<ResourceKey, PendingLoad> m_loadingTasks = new();

    /// <summary>
    /// 以路径和资源类型唯一标识一次加载
    /// </summary>
    private readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public readonly string Location;
        public readonly Type AssetType;

        public ResourceKey(string location, Type assetType)
        {
            Location = location;
            AssetType = assetType;
        }

        public bool Equals(ResourceKey other) =>
            StringComparer.Ordinal.Equals(Location, other.Location) && AssetType == other.AssetType;

        public override bool Equals(object obj) => obj is ResourceKey other && Equals(other);

        public override int GetHashCode() =>
            (StringComparer.Ordinal.GetHashCode(Location) * 397) ^ AssetType.GetHashCode();

        public override string ToString() => $"{Location}<{AssetType.Name}>";
    }

    /// <summary>
    /// 先登记后启动的共享资源加载请求
    /// </summary>
    private sealed class PendingLoad
    {
        /// <summary>
        /// 支持多个调用方等待的加载结果
        /// </summary>
        public readonly UniTaskCompletionSource<AssetHandle> Completion = new();
        /// <summary>
        /// 加载完成时需要保留的调用方引用数
        /// </summary>
        public int Waiters = 1;
    }

    /// <summary>
    /// 已加载的资源缓存 记录资源句柄与引用计数
    /// </summary>
    private readonly Dictionary<ResourceKey, (AssetHandle handle, int refCount)> m_cachedAssets = new();

    public ResourcesService(IResourcesPackageManager packageManager, ILogger<ResourcesService> logger)
    {
        m_packageManager = packageManager;
        m_logger = logger;
    }

    #region 服务生命周期

    public int Priority => FrameworkSettings.ResourcesStartupPriority;
    public string Description => "资源服务";
    public float Weight => FrameworkSettings.ResourcesStartupWeight;

    public async UniTask StartAsync(StartupContext context)
    {
        m_lifetimeToken = context.LifetimeToken;
        context.LifetimeToken.ThrowIfCancellationRequested();
        YooAssets.Initialize();
        await m_packageManager.InitializeLocalPackagesAsync(context);
        context.LifetimeToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        foreach (var pair in m_cachedAssets)
        {
            try
            {
                pair.Value.handle.Release();
            }
            catch (Exception e)
            {
                m_logger.Error($"释放资源{pair.Key}失败", e);
            }
        }

        m_cachedAssets.Clear();
        foreach (PendingLoad pending in m_loadingTasks.Values)
            pending.Completion.TrySetCanceled(new CancellationToken(true));
        m_loadingTasks.Clear();
    }

    #endregion

    /// <summary>
    /// 异步加载资源
    /// </summary>
    /// <param name="location">资源路径</param>
    /// <typeparam name="T">资源类型</typeparam>
    /// <returns>异步任务</returns>
    public async UniTask<T> LoadAssetAsync<T>(string location) where T : UnityEngine.Object
    {
        m_lifetimeToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(location))
            throw new ArgumentException("资源路径不能为空", nameof(location));

        ResourceKey key = new ResourceKey(location, typeof(T));

        if (m_cachedAssets.TryGetValue(key, out var cached))
        {
            var asset = CastAsset<T>(cached.handle, key);
            m_cachedAssets[key] = (cached.handle, cached.refCount + 1);
            return asset;
        }

        if (m_loadingTasks.TryGetValue(key, out var pending))
        {
            pending.Waiters++;
        }
        else
        {
            pending = new PendingLoad();
            m_loadingTasks.Add(key, pending);
            LoadPendingAssetAsync<T>(key, pending).Forget();
        }

        var handle = await pending.Completion.Task;
        try
        {
            return CastAsset<T>(handle, key);
        }
        catch
        {
            ReleaseAsset(location, typeof(T));
            throw;
        }
    }

    /// <summary>
    /// 加载资源并在唤醒调用方之前登记缓存与引用计数
    /// </summary>
    /// <param name="key">路径和类型组成的资源键</param>
    /// <param name="pending">已登记的共享加载请求</param>
    /// <returns>完成缓存登记的异步任务</returns>
    private async UniTask LoadPendingAssetAsync<T>(ResourceKey key, PendingLoad pending) where T : UnityEngine.Object
    {
        AssetHandle handle = null;
        try
        {
            var package = ResolvePackageByLocation(key.Location);
            handle = package.LoadAssetAsync<T>(GetAssetLocation(key.Location));
            await handle.WithCancellation(m_lifetimeToken);
            m_lifetimeToken.ThrowIfCancellationRequested();
            if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
                throw new InvalidOperationException($"加载资源失败 路径:{key.Location} 类型:{key.AssetType.Name} 错误:{handle.Error}");

            CastAsset<T>(handle, key);
            m_loadingTasks.Remove(key);
            m_cachedAssets.Add(key, (handle, pending.Waiters));
            var loadedHandle = handle;
            handle = null;
            pending.Completion.TrySetResult(loadedHandle);
        }
        catch (Exception exception)
        {
            m_loadingTasks.Remove(key);
            handle?.Release();
            if (exception is OperationCanceledException)
                pending.Completion.TrySetCanceled(new CancellationToken(true));
            else
                pending.Completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// 检查每个调用方请求的资源类型
    /// </summary>
    /// <typeparam name="T">期望的资源类型</typeparam>
    /// <param name="handle">已完成加载的资源句柄</param>
    /// <param name="key">路径和类型组成的资源键</param>
    /// <returns>类型匹配的资源</returns>
    private static T CastAsset<T>(AssetHandle handle, ResourceKey key) where T : UnityEngine.Object
    {
        if (handle.AssetObject is T asset)
            return asset;
        throw new InvalidCastException($"资源类型不匹配 路径:{key.Location} 期望:{key.AssetType.Name} 实际:{handle.AssetObject?.GetType().Name}");
    }

    /// <summary>
    /// 根据location找到对应Package
    /// </summary>
    private ResourcePackage ResolvePackageByLocation(string location)
    {
        var separatorIndex = location.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == location.Length - 1 || location.Contains('\\'))
            throw new KeyNotFoundException($"资源路径'{location}'必须使用'包名/包内路径'格式");

        var packageName = location.Substring(0, separatorIndex);

        if (YooAssets.TryGetPackage(packageName, out var package) && package != null)
            return package;

        throw new KeyNotFoundException($"未找到资源路径'{location}'对应的资源包'{packageName}'");
    }

    public void ReleaseAsset(string location, Type assetType)
    {
        if (assetType == null)
            throw new ArgumentNullException(nameof(assetType));

        ResourceKey key = new ResourceKey(location, assetType);
        if (!m_cachedAssets.TryGetValue(key, out var entry))
            return;

        var (handle, count) = entry;
        if (count > 1)
        {
            m_cachedAssets[key] = (handle, count - 1);
            return;
        }

        try
        {
            handle.Release();
            m_cachedAssets.Remove(key);
        }
        catch (Exception e)
        {
            m_logger.Error($"释放资源{key}失败", e);
        }
    }

    /// <summary>
    /// 通过对应资源包启动场景加载。
    /// 返回的句柄由调用方用于读取进度、选择激活时机和后续卸载。
    /// </summary>
    public SceneHandle LoadSceneAsync(
        string location,
        LoadSceneMode sceneMode = LoadSceneMode.Single,
        LocalPhysicsMode physicsMode = LocalPhysicsMode.None,
        bool allowSceneActivation = true,
        uint priority = 0)
    {
        m_lifetimeToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(location))
            throw new ArgumentException("场景路径不能为空", nameof(location));

        ResourcePackage package = ResolvePackageByLocation(location);
        return package.LoadSceneAsync(
            GetAssetLocation(location),
            sceneMode,
            physicsMode,
            allowSceneActivation,
            priority);
    }

    /// <summary>
    /// 卸载场景并等待YooAsset释放场景句柄引用
    /// </summary>
    public async UniTask UnloadSceneAsync(SceneHandle handle, CancellationToken cancellationToken = default)
    {
        if (handle == null)
            throw new ArgumentNullException(nameof(handle));

        var operation = handle.UnloadSceneAsync();
        await UniTask.WaitUntil(() => operation.IsDone, cancellationToken: cancellationToken);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"卸载场景失败 错误:{operation.Error}");
    }
}
