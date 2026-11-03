using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 配置表总服务
/// </summary>
[AutoRegister(typeof(IStartupService), AsSelf = false)]
internal class ConfigTableService : ISystem, IStartupService
{
    /// <summary>
    /// 容器提供的全部配置子服务 由总服务统一加载和清理
    /// </summary>
    private readonly IReadOnlyList<IConfigTableSubService> _subServices;
    /// <summary>
    /// 记录配置子服务的启动过程
    /// </summary>
    private readonly ILogger<ConfigTableService> _logger;

    /// <summary>
    /// 接收配置子服务集合和启动日志依赖
    /// </summary>
    public ConfigTableService(IReadOnlyList<IConfigTableSubService> subServices, ILogger<ConfigTableService> logger)
    {
        _subServices = subServices;
        _logger = logger;
    }

    #region 服务生命周期

    /// <summary>
    /// 由框架配置指定的配置表启动顺序
    /// </summary>
    public int Priority => FrameworkSettings.ConfigTableStartupPriority;
    /// <summary>
    /// 启动进度中显示的配置表服务名称
    /// </summary>
    public string Description => "配置表服务";
    /// <summary>
    /// 配置表加载占总体启动进度的权重
    /// </summary>
    public float Weight => FrameworkSettings.ConfigTableStartupWeight;

    /// <summary>
    /// 依次加载所有配置子服务 每次加载前检查生命周期取消
    /// </summary>
    public async UniTask StartAsync(StartupContext context)
    {
        foreach (var sub in _subServices)
        {
            context.LifetimeToken.ThrowIfCancellationRequested();
            _logger.Log($"开始加载配置子服务{sub.Name}");
            await sub.LoadAsync(context.LifetimeToken);
        }
    }

    /// <summary>
    /// 清空各配置子服务持有的表实例
    /// </summary>
    public void Dispose()
    {
        foreach (var sub in _subServices)
            sub.Clear();
    }

    #endregion
}
