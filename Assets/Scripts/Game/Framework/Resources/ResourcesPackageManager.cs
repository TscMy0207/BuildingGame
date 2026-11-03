using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

internal interface IResourcesPackageManager
{
    UniTask InitializeLocalPackagesAsync(StartupContext context);
}

/// <summary>
/// 资源包初始化流程管理器
/// </summary>
[AutoRegister(typeof(IResourcesPackageManager), AsSelf = false)]
internal class ResourcesPackageManager : ISystem, IResourcesPackageManager
{
    private readonly ILogger<ResourcesPackageManager> m_logger;
    private readonly ResourcesPackageOptionsFactory m_optionsFactory;
    private CancellationToken m_cancellation;

    public ResourcesPackageManager(ILogger<ResourcesPackageManager> logger, ResourcesPackageOptionsFactory optionsFactory)
    {
        m_logger = logger;
        m_optionsFactory = optionsFactory;
    }

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

    private async UniTask<ResourcePackage> InitializePackageManifestAsync(string packageName)
    {
        var package = await InitializePackageAsync(packageName);
        var version = await RequestPackageVersionAsync(package);
        m_logger.Log($"{package.PackageName}资源包版本:{version}");
        await LoadPackageManifestAsync(package, version);
        return package;
    }

    private async UniTask<ResourcePackage> InitializePackageAsync(string packageName)
    {
        if (!YooAssets.TryGetPackage(packageName, out var package)) package = YooAssets.CreatePackage(packageName);

        var operation = package.InitializePackageAsync(m_optionsFactory.Create(packageName));
        await operation.WithCancellation(m_cancellation);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"资源包{packageName}初始化失败:{operation.Error}");
        return package;
    }

    private async UniTask<string> RequestPackageVersionAsync(ResourcePackage package)
    {
        var operation = package.RequestPackageVersionAsync();
        await operation.WithCancellation(m_cancellation);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"资源包{package.PackageName}请求版本失败:{operation.Error}");
        return operation.PackageVersion;
    }

    private async UniTask LoadPackageManifestAsync(ResourcePackage package, string version)
    {
        var operation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, FrameworkSettings.ResourcesManifestLoadTimeout));
        await operation.WithCancellation(m_cancellation);
        if (operation.Status != EOperationStatus.Succeeded)
            throw new InvalidOperationException($"资源包{package.PackageName}加载清单失败:{operation.Error}");
    }
}
