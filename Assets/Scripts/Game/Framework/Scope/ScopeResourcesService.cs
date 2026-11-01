using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// Scope级资源服务 负责自动释放当前Scope加载过的资源
/// </summary>
public interface IScopeResourcesService
{
    /// <summary>
    /// 加载资源 并记录到当前Scope的资源所有权中
    /// </summary>
    UniTask<T> LoadAssetAsync<T>(string location) where T : UnityEngine.Object;
}

/// <summary>
/// Scope级资源服务 记录当前Scope加载的资源并在Scope释放时自动归还引用
/// </summary>
internal sealed class ScopeResourcesService : IScopeResourcesService, IDisposable
{
    private readonly IResourcesService m_resources;

    /// <summary>
    /// 当前Scope持有的资源路径和类型引用计数
    /// </summary>
    private readonly Dictionary<ResourceKey, int> m_ownedPaths = new();

    /// <summary>
    /// 标识当前Scope持有的一类资源
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
    }
    
    public ScopeResourcesService(IResourcesService resources)
    {
        m_resources = resources;
    }
    
    /// <summary>
    /// 异步加载资源 并记录到当前Scope的引用计数中
    /// </summary>
    public async UniTask<T> LoadAssetAsync<T>(string location) where T : UnityEngine.Object
    {
        var asset = await m_resources.LoadAssetAsync<T>(location);
        ResourceKey key = new ResourceKey(location, typeof(T));

        if (m_ownedPaths.TryGetValue(key, out var count))
            m_ownedPaths[key] = count + 1;
        else
            m_ownedPaths.Add(key, 1);

        return asset;
    }

    /// <summary>
    /// Scope销毁时自动释放当前Scope持有的全部资源引用
    /// </summary>
    public void Dispose()
    {
        foreach (var pair in m_ownedPaths)
        {
            for (var i = 0; i < pair.Value; i++)
                m_resources.ReleaseAsset(pair.Key.Location, pair.Key.AssetType);
        }

        m_ownedPaths.Clear();
    }
}
