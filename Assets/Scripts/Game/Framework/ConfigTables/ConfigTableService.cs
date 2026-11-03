using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 配置表总服务
/// </summary>
[AutoRegister(typeof(IStartupService), AsSelf = false)]
internal class ConfigTableService : ISystem, IStartupService
{
    private readonly IReadOnlyList<IConfigTableSubService> m_subServices;
    private readonly ILogger<ConfigTableService> m_logger;

    public ConfigTableService(IReadOnlyList<IConfigTableSubService> subServices, ILogger<ConfigTableService> logger)
    {
        m_subServices = subServices;
        m_logger = logger;
    }

    public int Priority => FrameworkSettings.ConfigTableStartupPriority;
    public string Description => "配置表服务";
    public float Weight => FrameworkSettings.ConfigTableStartupWeight;

    public async UniTask StartAsync(StartupContext context)
    {
        foreach (var sub in m_subServices)
        {
            context.LifetimeToken.ThrowIfCancellationRequested();
            m_logger.Log($"开始加载配置子服务{sub.Name}");
            await sub.LoadAsync(context.LifetimeToken);
        }
    }

    public void Dispose()
    {
        foreach (var sub in m_subServices)
            sub.Clear();
    }
}
