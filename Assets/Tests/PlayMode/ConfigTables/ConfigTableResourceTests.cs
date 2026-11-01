using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// 验证实际依赖注册 YooAsset模拟资源包与Luban加载能够协同工作
/// </summary>
public sealed class ConfigTableResourceTests
{
    /// <summary>
    /// 在实际运行循环中初始化资源服务并读取项目配置
    /// </summary>
    /// <returns>逐帧等待资源操作的测试流程</returns>
    [UnityTest]
    public IEnumerator LoadFrameworkTablesThroughYooAsset()
    {
        var builder = new ContainerBuilder();
        FrameworkComposition.RegisterDefaults(builder);
        using var container = builder.Build();
        var resources = container.Resolve<IResourcesService>();
        var config = container.Resolve<IFrameworkConfigTableService>();
        var configLoader = (IConfigTableSubService)config;
        try
        {
            var startup = ((IStartupService)resources).StartAsync(new StartupContext(CancellationToken.None, null)).AsTask();
            var deadline = UnityEngine.Time.realtimeSinceStartup + 30;
            while (!startup.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(startup.IsCompleted, Is.True, "资源初始化超时");
            startup.GetAwaiter().GetResult();

            var loading = configLoader.LoadAsync(CancellationToken.None).AsTask();
            while (!loading.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(loading.IsCompleted, Is.True, "配置加载超时");
            loading.GetAwaiter().GetResult();
            Assert.That(config.FrameworkTables, Is.Not.Null);
            Assert.That(config.FrameworkTables.TbUIPanel, Is.Not.Null);
            Assert.That(config.TryGetPanelConfig(int.MaxValue, out _), Is.False);
        }
        finally
        {
            configLoader.Clear();
            ((IStartupService)resources).Dispose();
            YooAsset.YooAssets.Destroy();
        }
    }
}
