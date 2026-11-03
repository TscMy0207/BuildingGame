using System;
using System.Collections.Generic;

/// <summary>
/// Framework全局配置
/// </summary>
internal static class FrameworkSettings
{
    // 启动服务配置
    // Priority越小越早启动 相同Priority之间的顺序取决于注册顺序
    // Weight为0到1 表示该服务在总进度中占的比例 总和会自动归一化
    // Weight为0表示该服务不参与进度条计算
    internal const int LoggingStartupPriority = 0;
    internal const float LoggingStartupWeight = 0.1f;
    internal const int ResourcesStartupPriority = 1;
    internal const float ResourcesStartupWeight = 0.2f;
    internal const int ConfigTableStartupPriority = 2;
    internal const float ConfigTableStartupWeight = 0.1f;
    internal const int UIStartupPriority = 3;
    internal const float UIStartupWeight = 0.1f;
    internal const int EventStartupPriority = 1;
    internal const float EventStartupWeight = 0f;
    internal const int SaveStartupPriority = 3;
    internal const float SaveStartupWeight = 0.05f;
    internal const int TimerStartupPriority = 1;
    internal const float TimerStartupWeight = 0f;
    internal const int AudioStartupPriority = 2;
    internal const float AudioStartupWeight = 0.05f;
    /// <summary>摄像机服务在业务模块启动前接入场景</summary>
    internal const int CameraStartupPriority = 3;
    /// <summary>摄像机宿主为场景对象 无需分配资源加载进度</summary>
    internal const float CameraStartupWeight = 0f;

    /// <summary>对象池在资源服务之后启动</summary>
    internal const int ObjectPoolStartupPriority = 2;
    /// <summary>对象池按需创建 启动不占加载进度</summary>
    internal const float ObjectPoolStartupWeight = 0f;
    /// <summary>每个预制体池最多保留的空闲实例数 不限制同时借出数量</summary>
    internal const int ObjectPoolMaxInactiveCount = 64;

    /// <summary>
    /// ZLogger滚动文件的大小阈值 单位MB 单条日志和后台批次可能使文件略超阈值
    /// </summary>
    internal const int LoggingMaxFileSizeMb = 2;
    /// <summary>
    /// 创建新日志文件时保留的文件总数 包含当前文件
    /// </summary>
    internal const int LoggingMaxFileCount = 5;

    // 资源包配置
    // 本地资源包名 对应BundleCollectorSetting中的包名
    internal static IReadOnlyList<string> ResourcesLocalPackageNames { get; } = new[] { "FrameworkConfig", "FrameworkAudio" };
    /// <summary>
    /// 本地资源包清单加载超时时间 单位秒
    /// </summary>
    internal const int ResourcesManifestLoadTimeout = 60;
    // 默认使用的YooAsset运行模式
#if UNITY_EDITOR
    internal const ResourcesPlayMode ResourcesDefaultPlayMode = ResourcesPlayMode.EditorSimulate;
#else
    internal const ResourcesPlayMode ResourcesDefaultPlayMode = ResourcesPlayMode.Offline;
#endif

    /// <summary>
    /// UI根预制体地址 使用包名/采集地址格式 配置项目资源后填写
    /// </summary>
    internal const string UIRootLocation = "";

    /// <summary>
    /// 音频根预制体地址 使用包名/采集地址格式
    /// </summary>
    internal const string AudioRootLocation = "FrameworkAudio/Assets/GameData/Audio/AudioRoot.prefab";

    /// <summary>
    /// 框架配置目录前缀 清单与二进制文件由此地址加载
    /// </summary>
    internal const string FrameworkConfigLocation = "FrameworkConfig/Assets/GameData/ConfigTables/Framework";

    /// <summary>当前Easy Save存档的容器格式版本</summary>
    internal const int SaveFormatVersion = 3;

    /// <summary>允许迁移的旧JSON存档格式版本</summary>
    internal const int LegacySaveFormatVersion = 2;
}

internal enum ResourcesPlayMode
{
    EditorSimulate,
    Offline,
}
