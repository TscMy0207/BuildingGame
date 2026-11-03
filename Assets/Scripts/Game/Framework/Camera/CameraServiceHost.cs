using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

/// <summary>
/// 持有场景输出摄像机 Brain 震动源及预设镜头 场景卸载时由服务解除引用
/// 不自动跨场景保留对象 主摄像机上的宿主会在服务启动和场景加载时尝试自动绑定
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera), typeof(CinemachineBrain), typeof(CinemachineImpulseSource))]
public class CameraServiceHost : MonoBehaviour
{
    /// <summary>
    /// 绑定时自动注册的场景镜头
    /// </summary>
    [SerializeField] private CameraEntry[] _cameras = Array.Empty<CameraEntry>();

    /// <summary>
    /// 绑定时选择的默认镜头 留空则不主动选择镜头
    /// </summary>
    [SerializeField] private string _defaultCameraId = "";

    /// <summary>
    /// 服务选中镜头使用的优先级 未注册镜头仍由Cinemachine自身规则管理
    /// </summary>
    [SerializeField] private int _activePriority = 100;

    /// <summary>
    /// 已注册但未选中镜头使用的优先级
    /// </summary>
    [SerializeField] private int _standbyPriority = -100;

    /// <summary>
    /// 缓存同一对象上的输出摄像机
    /// </summary>
    private Camera _outputCamera;
    /// <summary>
    /// 缓存同一对象上的镜头驱动器
    /// </summary>
    private CinemachineBrain _brain;
    /// <summary>
    /// 缓存同一对象上的震动源
    /// </summary>
    private CinemachineImpulseSource _impulseSource;

    /// <summary>
    /// 宿主停用或销毁前通知当前服务解除绑定
    /// </summary>
    internal event Action<CameraServiceHost> Unavailable;

    /// <summary>
    /// 当前独占宿主的服务 防止多个容器同时接管同一组组件
    /// </summary>
    internal ICameraService Owner { get; set; }

    /// <summary>
    /// 实际输出摄像机
    /// </summary>
    public Camera OutputCamera => _outputCamera != null ? _outputCamera : _outputCamera = GetComponent<Camera>();
    /// <summary>
    /// 负责镜头混合和最终输出的组件
    /// </summary>
    internal CinemachineBrain Brain => _brain != null ? _brain : _brain = GetComponent<CinemachineBrain>();
    /// <summary>
    /// 配置震动形状与频道的组件
    /// </summary>
    internal CinemachineImpulseSource ImpulseSource => _impulseSource != null ? _impulseSource : _impulseSource = GetComponent<CinemachineImpulseSource>();
    /// <summary>
    /// 宿主预设镜头的只读访问入口
    /// </summary>
    internal IReadOnlyList<CameraEntry> Cameras => _cameras;
    /// <summary>
    /// 宿主默认镜头标识
    /// </summary>
    internal string DefaultCameraId => _defaultCameraId;
    /// <summary>
    /// 选中镜头优先级
    /// </summary>
    internal int ActivePriority => _activePriority;
    /// <summary>
    /// 待机镜头优先级
    /// </summary>
    internal int StandbyPriority => _standbyPriority;
    /// <summary>
    /// 宿主及输出组件是否均已启用
    /// </summary>
    internal bool IsAvailable => isActiveAndEnabled && OutputCamera != null && OutputCamera.isActiveAndEnabled &&
        Brain != null && Brain.isActiveAndEnabled;

    /// <summary>
    /// 场景停用时立即断开服务对宿主及镜头的引用
    /// </summary>
    private void OnDisable() => Unavailable?.Invoke(this);

    /// <summary>
    /// 宿主中配置的一组镜头标识与场景组件引用
    /// </summary>
    [Serializable]
    public class CameraEntry
    {
        /// <summary>
        /// 当前宿主内唯一的镜头标识
        /// </summary>
        [SerializeField] private string _id;
        /// <summary>
        /// 场景中的顶层虚拟镜头
        /// </summary>
        [SerializeField] private CinemachineVirtualCameraBase _camera;

        /// <summary>
        /// 镜头标识
        /// </summary>
        public string Id => _id;
        /// <summary>
        /// 虚拟镜头组件
        /// </summary>
        public CinemachineVirtualCameraBase Camera => _camera;
    }
}
