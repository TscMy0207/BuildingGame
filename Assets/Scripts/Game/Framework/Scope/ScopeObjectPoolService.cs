using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

/// <summary>
/// 为每个模块创建独立池集合 使用模块容器注入并随Scope自动销毁
/// </summary>
internal sealed class ScopeObjectPoolService : IScopeObjectPoolService, IDisposable
{
    /// <summary>模块独占的池实现 不与全局服务共享实例</summary>
    private readonly ObjectPoolService _pools;

    /// <summary>
    /// 创建使用当前模块生命周期和容器的池服务
    /// </summary>
    /// <param name="resources">全局预制体资源加载服务</param>
    /// <param name="resolver">当前模块的依赖容器</param>
    /// <param name="module">模块生命周期</param>
    /// <param name="logger">对象池日志</param>
    public ScopeObjectPoolService(IResourcesAssetLoader resources, IObjectResolver resolver, IScopeModule module, ILogger<ObjectPoolService> logger)
    {
        _pools = new ObjectPoolService(resources, resolver, logger);
        _pools.Initialize(module.LifetimeToken);
    }

    /// <summary>
    /// 预热当前模块的池
    /// </summary>
    /// <param name="location">预制体地址</param>
    /// <param name="count">目标空闲数量</param>
    /// <param name="cancellationToken">本次等待取消令牌</param>
    /// <returns>预热任务</returns>
    public UniTask PrewarmAsync(string location, int count, CancellationToken cancellationToken = default) =>
        _pools.PrewarmAsync(location, count, cancellationToken);

    /// <summary>
    /// 获取当前模块独占且完成模块依赖注入的对象
    /// </summary>
    /// <param name="location">预制体地址</param>
    /// <param name="parent">目标父节点</param>
    /// <param name="cancellationToken">本次等待取消令牌</param>
    /// <returns>借出的实例</returns>
    public UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default) =>
        _pools.SpawnAsync(location, parent, cancellationToken);

    /// <summary>
    /// 仅回收本模块池借出的对象
    /// </summary>
    /// <param name="instance">借出的实例</param>
    /// <returns>是否成功回收</returns>
    public bool Despawn(GameObject instance) => _pools.Despawn(instance);

    /// <summary>
    /// 清理当前模块的指定池
    /// </summary>
    /// <param name="location">预制体地址</param>
    /// <returns>清理任务</returns>
    public UniTask ClearAsync(string location) => _pools.ClearAsync(location);

    /// <summary>
    /// 清理当前模块全部对象池
    /// </summary>
    /// <returns>清理任务</returns>
    public UniTask ClearAllAsync() => _pools.ClearAllAsync();

    /// <summary>
    /// Scope释放时立即停用并销毁模块全部池对象
    /// </summary>
    public void Dispose() => _pools.Dispose();
}
