using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

/// <summary>
/// 协调本地资源包初始化和清单加载的内部契约
/// </summary>
internal interface IResourcesPackageManager
{
    /// <summary>
    /// 初始化配置中的全部本地资源包并上报总体进度
    /// </summary>
    UniTask InitializeLocalPackagesAsync(StartupContext context);
}

/// <summary>
/// 资源包初始化流程管理器
/// </summary>
[AutoRegister(typeof(IResourcesPackageManager), AsSelf = false)]
internal class ResourcesPackageManager : ISystem, IResourcesPackageManager
{
    /// <summary>
    /// 记录资源包版本和初始化诊断信息
    /// </summary>
    private readonly ILogger<ResourcesPackageManager> m_logger;
    /// <summary>
    /// 按运行模式创建各资源包的初始化参数
    /// </summary>
    private readonly ResourcesPackageOptionsFactory m_optionsFactory;
    /// <summary>
    /// 贯穿资源包初始化与清单加载的服务生命周期令牌
    /// </summary>
    private CancellationToken m_cancellation;

    /// <summary>
    /// 接收资源包参数构建器和日志依赖
    /// </summary>
    public ResourcesPackageManager(ILogger<ResourcesPackageManager> logger, ResourcesPackageOptionsFactory optionsFactory)
    {
        m_logger = logger;
        m_optionsFactory = optionsFactory;
    }

    /// <summary>
    /// 依次初始化本地资源包 加载清单并汇报完成进度
    /// </summary>
    public async UniTask InitializeLocalPackagesAsync(StartupContext context)
    {
        m_cancellation = context.LifetimeToken;
        var progress = 0f;
        context.ReportProgress?.Invoke(progress, "初始化本地资源包");

        var packageCount = FrameworkSettings.ResourcesLocalPackageNames.Count;
        var step = packageCount > 0 ? 1f / packageCount : 0f;
        foreach (var packageName in FrameworkSettings.ResourcesLocalPackageNames)
        {
            var package = await InitializePackageManifestAsync(packageName);
            context.ReportProgress?.Invoke(progress += step, $"本地资源包'{package.PackageName}'初始化完成");
        }

        context.ReportProgress?.Invoke(1f, "本地资源包初始化完成");
    }

    /// <summary>
    /// 初始化指定包后查询版本并加载对应资源清单
    /// </summary>
    private async UniTask<ResourcePackage> InitializePackageManifestAsync(string packageName)
    {
        var package = await InitializePackageAsync(packageName);
        var version = await RequestPackageVersionAsync(package);
        m_logger.Log($"{package.PackageName}资源包版本:{version}");
        await LoadPackageManifestAsync(package, version);
        return package;
    }

    /// <summary>
    /// 复用或创建资源包 并以当前运行模式完成初始化
    /// </summary>
    private async UniTask<ResourcePackage> InitializePackageAsync(string packageName)
    {
        if (!YooAssets.TryGetPackage(packageName, out var package)) package = YooAssets.CreatePackage(packageName);

        var operation = package.InitializePackageAsync(m_optionsFactory.Create(packageName));
        await operation.WithCancellation(m_cancellation);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"资源包{packageName}初始化失败:{operation.Error}");
        return package;
    }

    /// <summary>
    /// 查询已初始化资源包的版本 失败时报告原始错误
    /// </summary>
    private async UniTask<string> RequestPackageVersionAsync(ResourcePackage package)
    {
        var operation = package.RequestPackageVersionAsync();
        await operation.WithCancellation(m_cancellation);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"资源包{package.PackageName}请求版本失败:{operation.Error}");
        return operation.PackageVersion;
    }

    /// <summary>
    /// 按给定版本加载资源清单 并支持服务生命周期取消
    /// </summary>
    private async UniTask LoadPackageManifestAsync(ResourcePackage package, string version)
    {
        var operation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, FrameworkSettings.ResourcesManifestLoadTimeout));
        await operation.WithCancellation(m_cancellation);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"资源包{package.PackageName}加载清单失败:{operation.Error}");
    }
}
