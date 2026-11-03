using System;
using YooAsset;

/// <summary>
/// 资源包初始化参数构建器
/// </summary>
[AutoRegister]
internal class ResourcesPackageOptionsFactory : ISystem
{
    /// <summary>
    /// 为指定资源包选择编辑器模拟或离线运行参数
    /// </summary>
    public InitializePackageOptions Create(string packageName)
    {
        return FrameworkSettings.ResourcesDefaultPlayMode switch
        {
            ResourcesPlayMode.EditorSimulate => CreateEditorSimulateOptions(packageName),
            ResourcesPlayMode.Offline => CreateOfflineOptions(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// 通过YooAsset模拟构建结果创建编辑器文件系统参数
    /// </summary>
    private static InitializePackageOptions CreateEditorSimulateOptions(string packageName)
    {
        var buildResult = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
        var fileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory);
        return new EditorSimulateModeOptions { EditorFileSystemParameters = fileSystemParameters };
    }

    /// <summary>
    /// 使用玩家包内置文件系统创建离线加载参数
    /// </summary>
    private static InitializePackageOptions CreateOfflineOptions()
    {
        var fileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
        return new OfflinePlayModeOptions { BuiltinFileSystemParameters = fileSystemParameters };
    }
}
