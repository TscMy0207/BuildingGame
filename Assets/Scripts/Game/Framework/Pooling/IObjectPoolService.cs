using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 按资源地址管理GameObject对象池 只能在Unity主线程使用
/// </summary>
public interface IObjectPoolService
{
    /// <summary>
    /// 预先创建指定数量的空闲实例 不触发取出回调
    /// </summary>
    /// <param name="location">YooAsset包名和预制体路径</param>
    /// <param name="count">期望的空闲实例数量 不超过框架配置的缓存上限</param>
    /// <param name="cancellationToken">仅取消本次等待 不取消共享资源加载</param>
    /// <returns>预热完成的异步任务</returns>
    UniTask PrewarmAsync(string location, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取并激活实例 重置为预制体的局部位置 旋转和缩放
    /// </summary>
    /// <param name="location">YooAsset包名和预制体路径</param>
    /// <param name="parent">目标父节点 为空时放在当前池的跨场景活动节点下</param>
    /// <param name="cancellationToken">仅取消本次等待 不取消共享资源加载</param>
    /// <returns>已经注入依赖且触发取出回调的实例</returns>
    UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 回收本服务借出的实例 重复回收 外部对象或其他Scope对象返回false
    /// </summary>
    /// <param name="instance">待回收的实例</param>
    /// <returns>是否成功回收</returns>
    bool Despawn(GameObject instance);

    /// <summary>
    /// 销毁指定池的活动及空闲实例 等待Unity销毁完成后归还资源引用
    /// </summary>
    /// <param name="location">池的预制体资源地址</param>
    /// <returns>资源引用归还完成的异步任务</returns>
    UniTask ClearAsync(string location);

    /// <summary>
    /// 清理当前服务的全部池 包含尚未完成的资源加载
    /// </summary>
    /// <returns>全部对象销毁并归还资源引用的异步任务</returns>
    UniTask ClearAllAsync();
}

/// <summary>
/// 模块独占的对象池服务 模块退出时自动销毁全部实例并归还资源引用
/// </summary>
public interface IScopeObjectPoolService : IObjectPoolService { }

/// <summary>
/// 池对象可选生命周期回调 在根节点和子节点上均可实现
/// </summary>
public interface IPooledObject
{
    /// <summary>
    /// 依赖注入和激活完成后处理本次取出 使用前清理上次业务状态
    /// </summary>
    void OnSpawned();

    /// <summary>
    /// 停用前解除本次借用的事件 定时器和临时状态
    /// </summary>
    void OnDespawned();
}
