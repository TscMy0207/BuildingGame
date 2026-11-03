using System;
using Newtonsoft.Json.Linq;

/// <summary>
/// 音频设置运行数据
/// </summary>
[Serializable]
[UnityEngine.Scripting.Preserve]
internal sealed class AudioSettingsSaveData
{
    /// <summary>
    /// 音乐开关
    /// </summary>
    public bool MusicEnabled = true;
    /// <summary>
    /// 音效开关
    /// </summary>
    public bool SfxEnabled = true;
    /// <summary>
    /// 音乐音量
    /// </summary>
    public float MusicVolume = 1f;
    /// <summary>
    /// 音效音量
    /// </summary>
    public float SfxVolume = 1f;
}

/// <summary>
/// 音频服务与设置界面共享的唯一设置来源
/// </summary>
public interface IAudioSettings
{
    /// <summary>
    /// 音乐是否开启
    /// </summary>
    bool MusicEnabled { get; }
    /// <summary>
    /// 音效是否开启
    /// </summary>
    bool SfxEnabled { get; }
    /// <summary>
    /// 当前音乐音量
    /// </summary>
    float MusicVolume { get; }
    /// <summary>
    /// 当前音效音量
    /// </summary>
    float SfxVolume { get; }

    /// <summary>
    /// 修改音乐开关并广播设置变化
    /// </summary>
    /// <param name="enabled">是否开启</param>
    void SetMusicEnabled(bool enabled);
    /// <summary>
    /// 修改音效开关并广播设置变化
    /// </summary>
    /// <param name="enabled">是否开启</param>
    void SetSfxEnabled(bool enabled);
    /// <summary>
    /// 修改音乐音量并广播设置变化
    /// </summary>
    /// <param name="volume">零到一之间的音量</param>
    void SetMusicVolume(float volume);
    /// <summary>
    /// 修改音效音量并广播设置变化
    /// </summary>
    /// <param name="volume">零到一之间的音量</param>
    void SetSfxVolume(float volume);
}

/// <summary>
/// 统一保存音频设置并通过Framework事件通知播放服务
/// </summary>
[AutoRegister(typeof(ISaveDataHandler), typeof(IAudioSettings), AsSelf = false)]
internal class AudioSettingsSaveDataHandler : ISystem, ISaveDataHandler, ILegacyJsonSaveDataHandler, IAudioSettings
{
    /// <summary>
    /// 音频存档数据段标识
    /// </summary>
    private const string DataKey = "audioSettings";
    /// <summary>
    /// 音频设置的共享事件服务
    /// </summary>
    private readonly IEventService _eventService;
    /// <summary>
    /// 当前音频设置
    /// </summary>
    private readonly AudioSettingsSaveData _data = new();

    /// <summary>
    /// 存档数据段标识
    /// </summary>
    public string Key => DataKey;
    /// <summary>
    /// 存档数据段版本
    /// </summary>
    public int CurrentVersion => 1;
    /// <summary>
    /// 音乐开关
    /// </summary>
    public bool MusicEnabled => _data.MusicEnabled;
    /// <summary>
    /// 音效开关
    /// </summary>
    public bool SfxEnabled => _data.SfxEnabled;
    /// <summary>
    /// 音乐音量
    /// </summary>
    public float MusicVolume => _data.MusicVolume;
    /// <summary>
    /// 音效音量
    /// </summary>
    public float SfxVolume => _data.SfxVolume;

    /// <summary>
    /// 创建音频设置处理器
    /// </summary>
    /// <param name="eventService">Framework事件服务</param>
    public AudioSettingsSaveDataHandler(IEventService eventService)
    {
        _eventService = eventService;
    }

    /// <summary>
    /// 重置音频开关与音量
    /// </summary>
    public void Reset()
    {
        _data.MusicEnabled = true;
        _data.SfxEnabled = true;
        _data.MusicVolume = 1f;
        _data.SfxVolume = 1f;
        PublishChanged();
    }

    /// <summary>
    /// 将当前音频设置快照写入Easy Save缓存
    /// </summary>
    /// <param name="file">待提交的文件缓存</param>
    public void Save(ES3File file) => file.Save(Key, new AudioSettingsSaveData
    {
        MusicEnabled = MusicEnabled,
        SfxEnabled = SfxEnabled,
        MusicVolume = MusicVolume,
        SfxVolume = SfxVolume
    });

    /// <summary>
    /// 载入音频设置并通知播放服务
    /// </summary>
    /// <param name="file">Easy Save文件缓存</param>
    /// <param name="savedVersion">存档数据段版本</param>
    public void Load(ES3File file, int savedVersion)
    {
        Apply(file.Load<AudioSettingsSaveData>(Key));
    }

    /// <summary>迁移旧JSON中的音频设置</summary>
    /// <param name="payload">旧音频设置数据</param>
    public void LoadLegacy(JObject payload) => Apply(payload.ToObject<AudioSettingsSaveData>());

    /// <summary>检查音频数据后更新共享设置并通知播放服务</summary>
    /// <param name="saved">待应用的音频设置</param>
    private void Apply(AudioSettingsSaveData saved)
    {
        if (saved == null || !IsValidVolume(saved.MusicVolume) || !IsValidVolume(saved.SfxVolume))
            throw new InvalidOperationException("音频设置存档数据无效");
        _data.MusicEnabled = saved.MusicEnabled;
        _data.SfxEnabled = saved.SfxEnabled;
        _data.MusicVolume = saved.MusicVolume;
        _data.SfxVolume = saved.SfxVolume;
        PublishChanged();
    }

    /// <summary>
    /// 修改音乐开关
    /// </summary>
    /// <param name="enabled">目标开关状态</param>
    public void SetMusicEnabled(bool enabled)
    {
        if (_data.MusicEnabled == enabled) return;
        _data.MusicEnabled = enabled;
        PublishChanged();
    }

    /// <summary>
    /// 修改音效开关
    /// </summary>
    /// <param name="enabled">目标开关状态</param>
    public void SetSfxEnabled(bool enabled)
    {
        if (_data.SfxEnabled == enabled) return;
        _data.SfxEnabled = enabled;
        PublishChanged();
    }

    /// <summary>
    /// 修改音乐音量
    /// </summary>
    /// <param name="volume">零到一之间的音量</param>
    public void SetMusicVolume(float volume)
    {
        if (!IsValidVolume(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
        if (_data.MusicVolume == volume) return;
        _data.MusicVolume = volume;
        PublishChanged();
    }

    /// <summary>
    /// 修改音效音量
    /// </summary>
    /// <param name="volume">零到一之间的音量</param>
    public void SetSfxVolume(float volume)
    {
        if (!IsValidVolume(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
        if (_data.SfxVolume == volume) return;
        _data.SfxVolume = volume;
        PublishChanged();
    }

    /// <summary>
    /// 判断音量是否为有效的零到一数值
    /// </summary>
    /// <param name="volume">待检查音量</param>
    /// <returns>音量是否有效</returns>
    private static bool IsValidVolume(float volume) =>
        !float.IsNaN(volume) && !float.IsInfinity(volume) && volume >= 0f && volume <= 1f;

    /// <summary>
    /// 广播设置变化以即时更新音频宿主
    /// </summary>
    private void PublishChanged() => _eventService.Publish(
        FrameworkEventKeys.AudioSettingsChanged, new AudioSettingsChangedEvent());
}
