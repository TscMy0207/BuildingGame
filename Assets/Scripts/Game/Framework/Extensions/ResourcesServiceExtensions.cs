using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>
/// 加载预制体并将本次资源引用绑定到实例销毁
/// </summary>
public static class ResourcesServiceExtensions
{
    /// <summary>
    /// 创建普通实例 销毁对象时自动归还本次加载引用
    /// </summary>
    /// <param name="resources">资源加载服务</param>
    /// <param name="location">预制体地址</param>
    /// <param name="parent">实例父节点</param>
    /// <param name="worldPositionStays">是否保留预制体世界坐标</param>
    /// <param name="cancellationToken">加载和创建期间的取消令牌</param>
    /// <returns>持有独立资源引用的实例</returns>
    public static UniTask<GameObject> LoadAndInstantiatePrefabAsync(this IResourcesAssetLoader resources,
        string location, Transform parent = null, bool worldPositionStays = false,
        CancellationToken cancellationToken = default) =>
        LoadAndCreateAsync(resources, location, null, parent, worldPositionStays, cancellationToken);

    /// <summary>
    /// 创建并注入实例 在Awake前完成依赖注入和引用绑定
    /// </summary>
    /// <param name="resources">资源加载服务</param>
    /// <param name="location">预制体地址</param>
    /// <param name="resolver">依赖解析器</param>
    /// <param name="parent">实例父节点</param>
    /// <param name="worldPositionStays">是否保留预制体世界坐标</param>
    /// <param name="cancellationToken">加载和创建期间的取消令牌</param>
    /// <returns>已注入并绑定资源引用的实例</returns>
    public static UniTask<GameObject> LoadAndInstantiatePrefabWithResolverAsync(this IResourcesAssetLoader resources,
        string location, IObjectResolver resolver, Transform parent = null, bool worldPositionStays = false,
        CancellationToken cancellationToken = default)
    {
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));
        return LoadAndCreateAsync(resources, location, resolver, parent, worldPositionStays, cancellationToken);
    }

    /// <summary>
    /// 加载成功后将取得的引用移交给实例创建流程
    /// </summary>
    private static async UniTask<GameObject> LoadAndCreateAsync(IResourcesAssetLoader resources, string location,
        IObjectResolver resolver, Transform parent, bool worldPositionStays, CancellationToken cancellationToken)
    {
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (string.IsNullOrWhiteSpace(location) || !location.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"预制体资源路径无效:{location}", nameof(location));
        cancellationToken.ThrowIfCancellationRequested();
        var prefab = await resources.LoadAssetAsync<GameObject>(location);
        return ResourceInstanceLifetime.Create(resources, location, prefab, resolver, parent, worldPositionStays, cancellationToken);
    }
}
