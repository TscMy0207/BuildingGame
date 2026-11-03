/// <summary>
/// Framework事件ID注册
/// 集中登记Framework层的事件ID 避免散落在各处导致ID冲突
/// ID 段位约定：
/// 0=None
/// 1-99=Framework 核心
/// 100-999=Framework 扩展/UI
/// 1000-1999=Core
/// 2000+=业务/插件
/// </summary>
public enum FrameworkEventRegistration
{
    /// <summary>
    /// 未分配的事件标识 不用于发布订阅
    /// </summary>
    None = 0,

    /// <summary>
    /// 音频设置发生改变
    /// </summary>
    AudioSettingsChanged = 100,
}

/// <summary>
/// Framework 事件键值注册
/// </summary>
public static class FrameworkEventKeys
{
    /// <summary>
    /// 音频设置发生改变
    /// </summary>
    public static readonly EventKey<AudioSettingsChangedEvent> AudioSettingsChanged = new((int)FrameworkEventRegistration.AudioSettingsChanged, nameof(FrameworkEventRegistration.AudioSettingsChanged));
}

#region 音频事件

/// <summary>
/// 音频设置发生改变的通知参数
/// 只做通知 具体值请读取 IAudioSettings
/// </summary>
public readonly struct AudioSettingsChangedEvent
{
}

#endregion
