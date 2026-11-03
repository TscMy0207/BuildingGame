using System;
using YooAsset;

/// <summary>
/// 资源包初始化参数构建器
/// </summary>
[AutoRegister]
internal class ResourcesPackageOptionsFactory : ISystem
{
    public InitializePackageOptions Create(string packageName)
    {
        return FrameworkSettings.ResourcesDefaultPlayMode switch
        {
            ResourcesPlayMode.EditorSimulate => CreateEditorSimulateOptions(packageName),
            ResourcesPlayMode.Offline => CreateOfflineOptions(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static InitializePackageOptions CreateEditorSimulateOptions(string packageName)
    {
        var buildResult = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
        var fileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory);
        return new EditorSimulateModeOptions { EditorFileSystemParameters = fileSystemParameters };
    }

    private static InitializePackageOptions CreateOfflineOptions()
    {
        var fileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
        return new OfflinePlayModeOptions { BuiltinFileSystemParameters = fileSystemParameters };
    }
}
