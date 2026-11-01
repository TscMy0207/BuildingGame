using VContainer;
using VContainer.Unity;

/// <summary>
/// 游戏根依赖容器 负责注册框架服务并在场景切换后保留容器
/// </summary>
public class GameLifetimeScope : LifetimeScope
{
    /// <summary>
    /// 注册框架默认服务及其生命周期
    /// </summary>
    /// <param name="builder">依赖容器构建器</param>
    protected override void Configure(IContainerBuilder builder)
    {
        base.Configure(builder);
        FrameworkComposition.RegisterDefaults(builder);
    }
}
