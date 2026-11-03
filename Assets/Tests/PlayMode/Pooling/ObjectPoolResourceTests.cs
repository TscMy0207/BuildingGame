using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// 验证对象池与项目现有YooAsset资源包的实际加载和释放链路
/// </summary>
public class ObjectPoolResourceTests
{
    /// <summary>
    /// 本次运行的容器
    /// </summary>
    private IObjectResolver _container;
    /// <summary>
    /// 项目真实资源服务
    /// </summary>
    private IResourcesService _resources;
    /// <summary>
    /// 项目真实对象池服务
    /// </summary>
    private IObjectPoolService _pool;

    /// <summary>
    /// 使用现有AudioRoot作为只预热不激活的资源依赖样本 不启动音频组件
    /// </summary>
    /// <returns>实际YooAsset资源加载及池清理验证</returns>
    [UnityTest]
    public IEnumerator PrewarmAndClearThroughYooAsset()
    {
        var builder = new ContainerBuilder();
        GameComposition.RegisterDefaults(builder);
        _container = builder.Build();
        _resources = _container.Resolve<IResourcesService>();
        _pool = _container.Resolve<IObjectPoolService>();
        var context = new StartupContext(CancellationToken.None, null);
        yield return Await(((IStartupService)_resources).StartAsync(context).AsTask());
        yield return Await(((IStartupService)_pool).StartAsync(context).AsTask());
        const string Location = "FrameworkAudio/Assets/GameData/Audio/AudioRoot.prefab";
        yield return Await(_pool.PrewarmAsync(Location, 2).AsTask());
        var hosts = Object.FindObjectsOfType<AudioServiceHost>(true);
        Assert.That(hosts.Length, Is.EqualTo(2));
        foreach (var host in hosts) Assert.That(host.gameObject.activeInHierarchy, Is.False);
        yield return Await(_pool.ClearAsync(Location).AsTask());
        foreach (var host in hosts) Assert.That(host == null, Is.True);
        Assert.That(Object.FindObjectsOfType<AudioServiceHost>(true), Is.Empty);
    }

    /// <summary>
    /// 先完成池清理再释放资源服务及包调度器
    /// </summary>
    /// <returns>资源清理流程</returns>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_pool != null) yield return Await(_pool.ClearAllAsync().AsTask());
        _container?.Dispose();
        YooAsset.YooAssets.Destroy();
        yield return null;
    }

    /// <summary>
    /// 等待真实资源操作并传播异常
    /// </summary>
    /// <param name="task">资源或池操作</param>
    /// <returns>带超时的逐帧等待</returns>
    private static IEnumerator Await(Task task)
    {
        var deadline = Time.realtimeSinceStartup + 30;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(task.IsCompleted, Is.True, "对象池资源联动测试超时");
        task.GetAwaiter().GetResult();
    }
}
