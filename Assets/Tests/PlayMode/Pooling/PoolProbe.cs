using UnityEngine;
using VContainer;

/// <summary>
/// 验证对象池注入 激活和复用顺序的测试组件
/// </summary>
public class PoolProbe : MonoBehaviour, IPooledObject
{
    /// <summary>
    /// 首次启用时是否已经完成注入
    /// </summary>
    public bool EnabledAfterInjection { get; private set; }
    /// <summary>
    /// 依赖注入次数
    /// </summary>
    public int InjectionCount { get; private set; }
    /// <summary>
    /// 本实例取出次数
    /// </summary>
    public int SpawnCount { get; private set; }
    /// <summary>
    /// 本实例归还次数
    /// </summary>
    public int DespawnCount { get; private set; }
    /// <summary>
    /// 所属容器提供的标记
    /// </summary>
    public string ContainerName { get; private set; }

    /// <summary>
    /// 记录依赖与注入次数
    /// </summary>
    /// <param name="dependency">所属容器的依赖</param>
    [Inject]
    public void Construct(PoolProbeDependency dependency)
    {
        InjectionCount++;
        ContainerName = dependency.Name;
    }

    /// <summary>
    /// 记录OnEnable时是否已经能够访问注入数据
    /// </summary>
    private void OnEnable() => EnabledAfterInjection = InjectionCount > 0;

    /// <summary>
    /// 记录本次借用
    /// </summary>
    public void OnSpawned() => SpawnCount++;

    /// <summary>
    /// 记录本次归还
    /// </summary>
    public void OnDespawned() => DespawnCount++;
}

/// <summary>
/// 为不同容器提供可区分的测试依赖
/// </summary>
public class PoolProbeDependency
{
    /// <summary>
    /// 容器标记
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// 创建容器标记
    /// </summary>
    /// <param name="name">标记名称</param>
    public PoolProbeDependency(string name) => Name = name;
}
