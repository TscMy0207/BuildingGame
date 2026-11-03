using System;
using DarkTonic.MasterAudio;
using UnityEngine;

/// <summary>
/// 持有Master Audio及其音乐控制器的跨场景宿主
/// </summary>
public class AudioServiceHost : MonoBehaviour
{
    /// <summary>
    /// 子节点上的Master Audio组件 其直接子节点由插件专用于声音组
    /// </summary>
    [SerializeField] private MasterAudio _audioManager;
    /// <summary>
    /// 宿主子节点上的音乐播放控制器
    /// </summary>
    [SerializeField] private PlaylistController _musicController;

    /// <summary>
    /// 当前宿主管理的声音组组件
    /// </summary>
    internal MasterAudio AudioManager => _audioManager;
    /// <summary>
    /// 当前宿主管理的音乐控制器
    /// </summary>
    internal PlaylistController MusicController => _musicController;

    /// <summary>
    /// 校验预制体引用并由宿主统一承担跨场景生命周期
    /// </summary>
    internal void Initialize()
    {
        if (_audioManager == null || !_audioManager.transform.IsChildOf(transform) ||
            _musicController == null || !_musicController.transform.IsChildOf(transform))
            throw new InvalidOperationException("AudioRoot必须配置子节点Master Audio和PlaylistController");
        DontDestroyOnLoad(gameObject);
    }
}
