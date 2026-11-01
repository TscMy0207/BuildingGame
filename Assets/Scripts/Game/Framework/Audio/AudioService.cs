using System;
using Cysharp.Threading.Tasks;
using DarkTonic.MasterAudio;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 通过Master Audio声音组和播放列表播放音频的契约
/// </summary>
public interface IAudioService
{
    /// <summary>
    /// 背景音乐是否开启
    /// </summary>
    bool MusicEnabled { get; }
    /// <summary>
    /// 音效是否开启
    /// </summary>
    bool SfxEnabled { get; }
    /// <summary>
    /// 播放声音组中的音效 未就绪 已关闭 名称无效或被并发规则拒绝时返回false
    /// </summary>
    /// <param name="soundGroup">AudioRoot中配置的声音组名称</param>
    /// <param name="volume">零到一之间的本次音量倍率</param>
    /// <returns>是否成功发起播放</returns>
    bool PlaySfx(string soundGroup, float volume = 1f);
    /// <summary>
    /// 切换音乐播放列表 音乐关闭时只记住请求 等待重新开启
    /// </summary>
    /// <param name="playlistName">AudioRoot中配置的非空播放列表名称</param>
    /// <returns>是否接受请求 未就绪或列表无效时返回false</returns>
    bool PlayMusic(string playlistName);
    /// <summary>
    /// 停止音乐并清除待恢复的播放列表
    /// </summary>
    void StopMusic();
}

/// <summary>
/// 负责Master Audio宿主的资源生命周期及Framework音频设置同步
/// </summary>
internal sealed class AudioService : IAudioService, IStartupService
{
    /// <summary>
    /// YooAsset音频根预制体地址
    /// </summary>
    private const string HostPrefab = FrameworkSettings.AudioRootLocation;
    /// <summary>
    /// 等待插件运行组件就绪的最长时间 单位秒
    /// </summary>
    private const float InitializationTimeout = 10f;

    /// <summary>音频设置唯一来源</summary>
    private readonly IAudioSettings _settings;
    /// <summary>宿主预制体的资源引用所有者</summary>
    private readonly IResourcesService _resources;
    /// <summary>设置变化通知来源</summary>
    private readonly IEventService _eventService;
    /// <summary>本次启动创建的根对象 用于失败回滚和正常释放</summary>
    private GameObject _instance;
    /// <summary>当前宿主及其插件组件引用</summary>
    private AudioServiceHost _host;
    /// <summary>需要在释放时解绑的设置订阅</summary>
    private EventHandle _eventHandle;
    /// <summary>
    /// 最近一次有效音乐请求 供重新开启音乐时恢复
    /// </summary>
    private string _requestedPlaylist;
    /// <summary>是否持有一次尚未归还的预制体资源引用</summary>
    private bool _prefabLoaded;
    private bool _started;
    private bool _disposed;
    private bool _wasMusicEnabled;

    /// <summary>
    /// 音频服务启动优先级
    /// </summary>
    public int Priority => FrameworkSettings.AudioStartupPriority;
    /// <summary>
    /// 启动进度描述
    /// </summary>
    public string Description => "音频服务";
    /// <summary>
    /// 启动进度权重
    /// </summary>
    public float Weight => FrameworkSettings.AudioStartupWeight;
    /// <summary>
    /// 当前音乐开关
    /// </summary>
    public bool MusicEnabled => _settings.MusicEnabled;
    /// <summary>
    /// 当前音效开关
    /// </summary>
    public bool SfxEnabled => _settings.SfxEnabled;

    /// <summary>
    /// 创建音频服务并复用框架现有设置与事件
    /// </summary>
    /// <param name="settings">音频设置唯一来源</param>
    /// <param name="resources">宿主资源加载服务</param>
    /// <param name="eventService">设置变化事件服务</param>
    public AudioService(IAudioSettings settings, IResourcesService resources, IEventService eventService)
    {
        _settings = settings;
        _resources = resources;
        _eventService = eventService;
    }

    /// <summary>
    /// 加载宿主并等待Master Audio和PlaylistController完成初始化
    /// </summary>
    /// <param name="context">启动取消令牌和进度回调</param>
    /// <returns>音频组件就绪的异步任务</returns>
    public async UniTask StartAsync(StartupContext context)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        if (_started) throw new InvalidOperationException("音频服务不能重复启动");
        _started = true;
        var cancellationToken = context.LifetimeToken;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress?.Invoke(0.2f, "加载AudioRoot");
            // 加载后先检查取消和重复实例 再实例化 避免Awake修改全局插件状态
            var prefab = await _resources.LoadAssetAsync<GameObject>(HostPrefab);
            _prefabLoaded = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
            if (MasterAudio.SafeInstance != null)
                throw new InvalidOperationException("音频服务启动前已存在Master Audio 请移除场景中重复的音频根节点");
            if (prefab == null) throw new InvalidOperationException($"音频根预制体为空:{HostPrefab}");

            _instance = Object.Instantiate(prefab);
            _host = _instance.GetComponent<AudioServiceHost>();
            if (_host == null) throw new InvalidOperationException($"音频根预制体缺少AudioServiceHost:{HostPrefab}");
            _host.Initialize();
            var deadline = Time.realtimeSinceStartup + InitializationTimeout;
            while (!IsReady)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
                if (_host == null || Time.realtimeSinceStartup >= deadline)
                    throw new InvalidOperationException("Master Audio初始化失败或超时 请检查AudioRoot预制体");
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _wasMusicEnabled = _settings.MusicEnabled;
            ApplySettings();
            _eventHandle = _eventService.Subscribe(FrameworkEventKeys.AudioSettingsChanged, OnSettingsChanged);
            context.ReportProgress?.Invoke(1f, "音频服务初始化完成");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// 解除事件 停止播放并释放宿主及其资源引用 可重复调用
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (_eventHandle.IsValid) _eventService.Unsubscribe(_eventHandle);
        _eventHandle = default;
        _requestedPlaylist = null;
        if (_host != null && MasterAudio.SafeInstance == _host.AudioManager && MasterAudio.SoundsReady)
            MasterAudio.StopEverything();
        if (_instance != null) Object.Destroy(_instance);
        _instance = null;
        _host = null;
        if (_prefabLoaded)
        {
            _resources.ReleaseAsset<GameObject>(HostPrefab);
            _prefabLoaded = false;
        }
    }

    /// <summary>
    /// 使用声音组的变体 随机化和并发规则播放一次音效
    /// </summary>
    /// <param name="soundGroup">声音组名称</param>
    /// <param name="volume">本次播放的音量倍率</param>
    /// <returns>是否成功发起播放</returns>
    public bool PlaySfx(string soundGroup, float volume = 1f)
    {
        if (!IsReady || !SfxEnabled || string.IsNullOrWhiteSpace(soundGroup) ||
            float.IsNaN(volume) || float.IsInfinity(volume) || MasterAudio.GrabGroup(soundGroup, false) == null)
            return false;
        return MasterAudio.PlaySoundAndForget(soundGroup, Mathf.Clamp01(volume));
    }

    /// <summary>
    /// 请求播放音乐列表 循环 随机和过渡由插件配置控制
    /// </summary>
    /// <param name="playlistName">非空音乐列表名称</param>
    /// <returns>是否接受有效的播放请求</returns>
    public bool PlayMusic(string playlistName)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(playlistName)) return false;
        var playlist = MasterAudio.GrabPlaylist(playlistName, false);
        if (playlist == null || playlist.MusicSettings.Count == 0) return false;
        _requestedPlaylist = playlistName;
        if (MusicEnabled) _host.MusicController.StartPlaylist(playlistName);
        return true;
    }

    /// <summary>
    /// 显式停止音乐并取消开关恢复行为
    /// </summary>
    public void StopMusic()
    {
        _requestedPlaylist = null;
        if (IsReady) _host.MusicController.StopPlaylist();
    }

    /// <summary>
    /// 判断当前插件实例是否归本服务所有且已完成初始化
    /// </summary>
    private bool IsReady => !_disposed && _host != null && _host.AudioManager != null &&
        MasterAudio.SafeInstance == _host.AudioManager && MasterAudio.SoundsReady &&
        _host.MusicController != null && _host.MusicController.ControllerIsReady;

    /// <summary>
    /// 接收音频设置变化通知
    /// </summary>
    /// <param name="args">设置变化事件</param>
    private void OnSettingsChanged(AudioSettingsChangedEvent args) => ApplySettings();

    /// <summary>
    /// 将设置同步到插件 音乐关闭时停止 开启时恢复最近一次有效请求
    /// </summary>
    private void ApplySettings()
    {
        if (!IsReady) return;
        MasterAudio.MasterVolumeLevel = _settings.SfxVolume;
        MasterAudio.PlaylistMasterVolume = _settings.MusicVolume;
        MasterAudio.MixerMuted = !SfxEnabled;
        MasterAudio.PlaylistsMuted = !MusicEnabled;
        if (_wasMusicEnabled == MusicEnabled) return;
        _wasMusicEnabled = MusicEnabled;
        if (!MusicEnabled) _host.MusicController.StopPlaylist();
        else if (_requestedPlaylist != null) _host.MusicController.StartPlaylist(_requestedPlaylist);
    }
}
