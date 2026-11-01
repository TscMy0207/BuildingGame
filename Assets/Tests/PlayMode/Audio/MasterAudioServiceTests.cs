using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DarkTonic.MasterAudio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// 通过实际YooAsset资源及Master Audio运行组件验证音频服务
/// </summary>
public sealed class MasterAudioServiceTests
{
    /// <summary>每个测试独立创建和释放的框架容器</summary>
    private IObjectResolver _container;
    /// <summary>加载实际音频预制体的资源服务</summary>
    private IResourcesService _resources;
    /// <summary>驱动设置通知的真实事件服务</summary>
    private IEventService _events;
    /// <summary>真实音频设置实例</summary>
    private IAudioSettings _settings;
    /// <summary>被测试的音频服务</summary>
    private IAudioService _audio;
    /// <summary>仅当场景缺少监听器时创建的测试对象</summary>
    private GameObject _listenerObject;
    /// <summary>仅用于本次测试的动态声音组定义</summary>
    private GameObject _dynamicGroup;
    /// <summary>不写入项目资源的静音测试片段</summary>
    private AudioClip _clip;

    /// <summary>
    /// 只启动音频依赖 避免测试受尚未配置的UI入口影响
    /// </summary>
    /// <returns>逐帧等待资源包初始化</returns>
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        var builder = new ContainerBuilder();
        FrameworkComposition.RegisterDefaults(builder);
        _container = builder.Build();
        _resources = _container.Resolve<IResourcesService>();
        _events = _container.Resolve<IEventService>();
        _settings = _container.Resolve<IAudioSettings>();
        _audio = _container.Resolve<IAudioService>();
        var context = new StartupContext(CancellationToken.None, null);
        yield return Await(((IStartupService)_events).StartAsync(context).AsTask());
        yield return Await(((IStartupService)_resources).StartAsync(context).AsTask());
        if (Object.FindObjectOfType<AudioListener>() == null)
            _listenerObject = new GameObject("AudioTestListener", typeof(AudioListener));
        // 使用内存中的静音片段 验证播放状态而不干扰用户音频
        _clip = AudioClip.Create("AudioTestClip", 88200, 1, 44100, false);
    }

    /// <summary>
    /// 销毁测试宿主并等待插件完成静态实例清理
    /// </summary>
    /// <returns>等待Unity延迟销毁完成</returns>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        ((IStartupService)_audio)?.Dispose();
        if (_dynamicGroup != null) Object.Destroy(_dynamicGroup);
        if (_listenerObject != null) Object.Destroy(_listenerObject);
        yield return null;
        if (_clip != null) Object.Destroy(_clip);
        _container?.Dispose();
        YooAsset.YooAssets.Destroy();
    }

    /// <summary>
    /// 验证真实声音组和音乐列表播放 以及设置事件对插件的同步
    /// </summary>
    /// <returns>等待播放和状态更新的测试流程</returns>
    [UnityTest]
    public IEnumerator NativePlaybackAndSettings()
    {
        Assert.That(_audio.PlaySfx("Missing"), Is.False);
        Assert.That(_audio.PlayMusic("Missing"), Is.False);
        yield return StartAudio();
        AddTestAudio();
        Assert.That(_audio.PlaySfx("AudioTestSfx", 0.5f), Is.True);
        yield return null;
        Assert.That(MasterAudio.GrabGroup("AudioTestSfx").GetComponentInChildren<AudioSource>().isPlaying, Is.True);
        Assert.That(_audio.PlayMusic("AudioTestMusic"), Is.True);
        var music = PlaylistController.InstanceByName("FrameworkMusic");
        yield return WaitForMusic(music);

        _settings.SetMusicVolume(0.35f);
        _settings.SetSfxVolume(0.6f);
        Assert.That(MasterAudio.PlaylistMasterVolume, Is.EqualTo(0.35f).Within(0.001f));
        Assert.That(MasterAudio.MasterVolumeLevel, Is.EqualTo(0.6f).Within(0.001f));
        _settings.SetSfxEnabled(false);
        Assert.That(MasterAudio.MixerMuted, Is.True);
        Assert.That(_audio.PlaySfx("AudioTestSfx"), Is.False);
        _settings.SetSfxEnabled(true);
        Assert.That(MasterAudio.MixerMuted, Is.False);
        _settings.SetMusicEnabled(false);
        Assert.That(music.CurrentSongIsPlaying, Is.False);
        Assert.That(MasterAudio.PlaylistsMuted, Is.True);
        _settings.SetMusicEnabled(true);
        yield return WaitForMusic(music);
        Assert.That(music.CurrentSongIsPlaying, Is.True, "重新开启应重播同一列表");
        Assert.That(MasterAudio.PlaylistsMuted, Is.False);
        Assert.That(_audio.PlaySfx("Missing"), Is.False);
        Assert.That(_audio.PlayMusic("Missing"), Is.False);

        ((IStartupService)_audio).Dispose();
        yield return null;
        Assert.That(MasterAudio.SafeInstance, Is.Null);
        Assert.That(Object.FindObjectOfType<AudioServiceHost>(), Is.Null);
        Assert.That(_audio.PlaySfx("AudioTestSfx"), Is.False);
        _settings.SetMusicVolume(0.8f);
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// 验证关闭期间的请求可以恢复 但显式停止后不会被开关重新触发
    /// </summary>
    /// <returns>逐帧观察音乐状态</returns>
    [UnityTest]
    public IEnumerator StopMusicCancelsDeferredPlayback()
    {
        yield return StartAudio();
        AddTestAudio();
        var music = PlaylistController.InstanceByName("FrameworkMusic");
        _settings.SetMusicEnabled(false);
        Assert.That(_audio.PlayMusic("AudioTestMusic"), Is.True);
        Assert.That(music.CurrentSongIsPlaying, Is.False);
        _settings.SetMusicEnabled(true);
        yield return WaitForMusic(music);
        Assert.That(music.CurrentSongIsPlaying, Is.True);
        _audio.StopMusic();
        _settings.SetMusicEnabled(false);
        _settings.SetMusicEnabled(true);
        yield return null;
        Assert.That(music.CurrentSongIsPlaying, Is.False);
    }

    /// <summary>
    /// 验证加载期间取消会归还资源且不会创建插件实例
    /// </summary>
    /// <returns>等待被取消的启动结束</returns>
    [UnityTest]
    public IEnumerator CancellationDuringLoadLeavesNoHost()
    {
        using var cancellation = new CancellationTokenSource();
        var startup = ((IStartupService)_audio).StartAsync(
            new StartupContext(cancellation.Token, (_, _) => cancellation.Cancel())).AsTask();
        while (!startup.IsCompleted) yield return null;
        Assert.Throws<OperationCanceledException>(() => startup.GetAwaiter().GetResult());
        yield return null;
        Assert.That(MasterAudio.SafeInstance, Is.Null);
        Assert.That(Object.FindObjectOfType<AudioServiceHost>(), Is.Null);
        Assert.That(_audio.PlayMusic("AudioTestMusic"), Is.False);
    }

    /// <summary>
    /// 验证重复启动者被拒绝且不会停止或销毁已经运行的插件
    /// </summary>
    /// <returns>等待重复启动请求结束</returns>
    [UnityTest]
    public IEnumerator DuplicateServiceDoesNotDestroyExistingHost()
    {
        yield return StartAudio();
        AddTestAudio();
        Assert.That(_audio.PlayMusic("AudioTestMusic"), Is.True);
        var existing = MasterAudio.SafeInstance;
        var duplicate = (IStartupService)Activator.CreateInstance(_audio.GetType(), _settings, _resources, _events);
        var startup = duplicate.StartAsync(new StartupContext(CancellationToken.None, null)).AsTask();
        while (!startup.IsCompleted) yield return null;
        Assert.Throws<InvalidOperationException>(() => startup.GetAwaiter().GetResult());
        duplicate.Dispose();
        yield return null;
        Assert.That(MasterAudio.SafeInstance, Is.SameAs(existing));
        Assert.That(PlaylistController.InstanceByName("FrameworkMusic").CurrentSongIsPlaying, Is.True);
        Assert.That(Object.FindObjectsOfType<AudioServiceHost>().Length, Is.EqualTo(1));
    }

    /// <summary>
    /// 等待音频服务通过真实资源加载完成初始化
    /// </summary>
    /// <returns>音频启动测试流程</returns>
    private IEnumerator StartAudio() =>
        Await(((IStartupService)_audio).StartAsync(new StartupContext(CancellationToken.None, null)).AsTask());

    /// <summary>
    /// 向插件添加仅存在于本次测试运行中的声音组和音乐列表
    /// </summary>
    private void AddTestAudio()
    {
        _dynamicGroup = new GameObject("AudioTestSfx");
        var group = _dynamicGroup.AddComponent<DynamicSoundGroup>();
        var variationObject = new GameObject("TestVariation", typeof(AudioSource), typeof(DynamicGroupVariation));
        variationObject.transform.SetParent(_dynamicGroup.transform);
        var source = variationObject.GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = _clip;
        group.groupVariations.Add(variationObject.GetComponent<DynamicGroupVariation>());
        Assert.That(MasterAudio.CreateSoundGroup(group, null), Is.Not.Null);
        MasterAudio.CreatePlaylist(new MasterAudio.Playlist { playlistName = "AudioTestMusic" }, true);
        MasterAudio.AddSongToPlaylist("AudioTestMusic", _clip, true);
    }

    /// <summary>
    /// 等待DSP调度进入实际播放 避免将同一音频采样周期内的重启误判为失败
    /// </summary>
    /// <param name="music">音乐控制器</param>
    /// <returns>带超时的实际播放检查</returns>
    private static IEnumerator WaitForMusic(PlaylistController music)
    {
        var deadline = Time.realtimeSinceStartup + 3f;
        while ((!music.CurrentSongIsPlaying || !music.ActiveAudioSource.isPlaying) &&
               Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(music.CurrentSongIsPlaying, Is.True, "音乐调度未开始");
        Assert.That(music.ActiveAudioSource.isPlaying, Is.True, "音乐AudioSource未播放");
    }

    /// <summary>
    /// 等待异步任务完成并保留原始异常
    /// </summary>
    /// <param name="task">待检查的异步任务</param>
    /// <returns>带超时的逐帧等待流程</returns>
    private static IEnumerator Await(Task task)
    {
        var deadline = Time.realtimeSinceStartup + 30f;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(task.IsCompleted, Is.True, "音频测试等待超时");
        task.GetAwaiter().GetResult();
    }
}
