using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>
/// IResourcesService的预制体实例化扩展
/// 加载失败或实例化失败时会归还资源引用
/// </summary>
public static class ResourcesServiceExtensions
{
    public static async UniTask<GameObject> LoadAndInstantiatePrefabAsync(this IResourcesService resources, string location, Transform parent = null, bool worldPositionStays = false)
    {
        var prefab = await LoadPrefabAssetAsync(resources, location);
        try
        {
            return Object.Instantiate(prefab, parent, worldPositionStays);
        }
        catch
        {
            resources.ReleaseAsset<GameObject>(location);
            throw;
        }
    }

    public static async UniTask<GameObject> LoadAndInstantiatePrefabWithResolverAsync(this IResourcesService resources, string location, IObjectResolver resolver, Transform parent = null, bool worldPositionStays = false, CancellationToken cancellationToken = default)
    {
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));

        cancellationToken.ThrowIfCancellationRequested();
        var prefab = await LoadPrefabAssetAsync(resources, location);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return resolver.Instantiate(prefab, parent, worldPositionStays);
        }
        catch
        {
            resources.ReleaseAsset<GameObject>(location);
            throw;
        }
    }

    private static async UniTask<GameObject> LoadPrefabAssetAsync(IResourcesService resources, string location)
    {
        if (resources == null)
            throw new ArgumentNullException(nameof(resources));
        if (string.IsNullOrWhiteSpace(location) || !location.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Prefab资源路径无效: {location}", nameof(location));

        var prefab = await resources.LoadAssetAsync<GameObject>(location);
        if (prefab != null)
            return prefab;

        resources.ReleaseAsset<GameObject>(location);
        throw new InvalidOperationException($"Prefab资源为空: {location}");
    }
}
