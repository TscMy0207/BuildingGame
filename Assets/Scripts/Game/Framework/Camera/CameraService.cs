using System;
using System.Collections.Generic;
using System.Threading;
using Cinemachine;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 通过Cinemachine提供镜头注册 切换 目标设置和震动 并管理场景引用生命周期
/// 仅借用宿主及镜头 不创建或销毁场景资源
/// </summary>
[AutoRegister(typeof(ICameraService), typeof(IStartupService), AsSelf = false)]
internal class CameraService : ISystem, ICameraService, IStartupService
{
    /// <summary>当前宿主下由服务接管的镜头及原始状态</summary>
    private readonly Dictionary<string, CameraState> _cameras = new(StringComparer.Ordinal);
    /// <summary>当前借用的场景宿主</summary>
    private CameraServiceHost _host;
    /// <summary>绑定宿主之前的默认混合配置</summary>
    private CinemachineBlendDefinition _originalBlend;
    /// <summary>最近一次接受的切换请求</summary>
    private string _currentCameraId;
    /// <summary>基础服务启动上下文中的生命周期令牌</summary>
    private CancellationToken _lifetimeToken;
    /// <summary>服务是否已经启动</summary>
    private bool _started;
    /// <summary>服务是否已经永久释放</summary>
    private bool _disposed;

    #region 服务生命周期

    /// <summary>摄像机服务启动顺序</summary>
    public int Priority => FrameworkSettings.CameraStartupPriority;
    /// <summary>启动进度说明</summary>
    public string Description => "摄像机服务";
    /// <summary>启动进度权重</summary>
    public float Weight => FrameworkSettings.CameraStartupWeight;

    /// <summary>监听场景生命周期并尝试绑定主摄像机上的宿主 无宿主时保持待绑定状态</summary>
    /// <param name="context">启动取消令牌和进度回调</param>
    /// <returns>同步完成的启动任务</returns>
    public UniTask StartAsync(StartupContext context)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraService));
        if (_started) throw new InvalidOperationException("摄像机服务不能重复启动");
        if (context == null) throw new ArgumentNullException(nameof(context));
        context.LifetimeToken.ThrowIfCancellationRequested();
        _lifetimeToken = context.LifetimeToken;
        _started = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        try
        {
            TryBindMainCamera();
            context.ReportProgress?.Invoke(1f, IsReady ? "摄像机宿主已绑定" : "等待场景摄像机宿主");
            return UniTask.CompletedTask;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>解除场景监听并恢复所有借用对象 可重复调用且不销毁宿主</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        ReleaseHost();
    }

    #endregion

    /// <summary>服务生命周期与宿主组件是否均可用</summary>
    public bool IsReady => _started && !_disposed && !_lifetimeToken.IsCancellationRequested && _host != null && _host.IsAvailable;
    /// <summary>当前可用的输出摄像机</summary>
    public Camera OutputCamera => IsReady ? _host.OutputCamera : null;
    /// <summary>服务请求的有效目标镜头标识 不替代Brain实际输出状态</summary>
    public string CurrentCameraId => IsReady && _currentCameraId != null &&
        _cameras.TryGetValue(_currentCameraId, out var state) && state.Camera != null ? _currentCameraId : null;

    /// <summary>校验全部预设后替换宿主 避免无效配置破坏当前绑定</summary>
    /// <param name="host">待绑定的场景宿主</param>
    public void BindHost(CameraServiceHost host)
    {
        EnsureStarted();
        if (host == null) throw new ArgumentNullException(nameof(host));
        if (!host.IsAvailable) throw new InvalidOperationException("摄像机宿主及输出组件必须处于启用状态");
        if (host == _host) return;
        if (host.Owner != null) throw new InvalidOperationException("摄像机宿主已被其他服务接管");
        if (host.ActivePriority <= host.StandbyPriority)
            throw new InvalidOperationException("摄像机选中优先级必须大于待机优先级");

        var pending = new Dictionary<string, CameraState>(StringComparer.Ordinal);
        foreach (var entry in host.Cameras)
        {
            if (entry == null) throw new InvalidOperationException("摄像机宿主存在空镜头配置");
            ValidateRegistration(pending, host, entry.Id, entry.Camera);
            pending.Add(entry.Id, new CameraState(entry.Camera));
        }
        if (!string.IsNullOrWhiteSpace(host.DefaultCameraId) &&
            (!pending.TryGetValue(host.DefaultCameraId, out var defaultState) || !defaultState.Camera.isActiveAndEnabled))
            throw new InvalidOperationException($"默认镜头未注册或未启用:{host.DefaultCameraId}");

        ReleaseHost();
        _host = host;
        host.Owner = this;
        _originalBlend = host.Brain.m_DefaultBlend;
        host.Unavailable += OnHostUnavailable;
        foreach (var pair in pending)
        {
            // 原宿主和新宿主可能引用同一镜头 必须在旧状态恢复后重新取得原始值
            var state = new CameraState(pair.Value.Camera);
            _cameras.Add(pair.Key, state);
            SetPriority(state.Camera, host.StandbyPriority);
        }
        if (!string.IsNullOrWhiteSpace(host.DefaultCameraId)) SwitchTo(host.DefaultCameraId);
    }

    /// <summary>按宿主身份解除绑定 避免旧场景回调解绑新宿主</summary>
    /// <param name="host">请求解绑的宿主</param>
    public void UnbindHost(CameraServiceHost host)
    {
        if (ReferenceEquals(host, _host)) ReleaseHost();
    }

    /// <summary>接管动态镜头并保存接管前的目标与优先级</summary>
    /// <param name="id">唯一镜头标识</param>
    /// <param name="camera">顶层虚拟镜头</param>
    public void RegisterCamera(string id, CinemachineVirtualCameraBase camera)
    {
        EnsureStarted();
        if (!IsReady) throw new InvalidOperationException("摄像机服务尚未绑定可用宿主");
        ValidateRegistration(_cameras, _host, id, camera);
        _cameras.Add(id, new CameraState(camera));
        SetPriority(camera, _host.StandbyPriority);
    }

    /// <summary>归还镜头原始状态 当前镜头移除后选择宿主默认镜头或其他可用镜头</summary>
    /// <param name="id">镜头标识</param>
    /// <returns>是否注销了已有镜头</returns>
    public bool UnregisterCamera(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_cameras.TryGetValue(id, out var state)) return false;
        _cameras.Remove(id);
        state.Restore();
        if (_currentCameraId == id)
        {
            _currentCameraId = null;
            SelectFallbackCamera();
        }
        return true;
    }

    /// <summary>通过优先级选择镜头 保留Cinemachine的混合和Timeline控制行为</summary>
    /// <param name="id">目标镜头标识</param>
    /// <returns>是否接受有效请求</returns>
    public bool SwitchTo(string id)
    {
        if (!TryGetCamera(id, out var state) || !state.Camera.isActiveAndEnabled) return false;
        if (_currentCameraId != null && _cameras.TryGetValue(_currentCameraId, out var previous) && previous.Camera != null)
            SetPriority(previous.Camera, _host.StandbyPriority);
        SetPriority(state.Camera, _host.ActivePriority);
        _currentCameraId = id;
        return true;
    }

    /// <summary>向注册镜头转发跟随及注视目标 具体构图由该镜头的管线组件决定</summary>
    /// <param name="id">镜头标识</param>
    /// <param name="follow">跟随目标 可为空</param>
    /// <param name="lookAt">注视目标 可为空</param>
    /// <returns>是否设置成功</returns>
    public bool SetTargets(string id, Transform follow, Transform lookAt)
    {
        if (!TryGetCamera(id, out var state)) return false;
        state.Camera.Follow = follow;
        state.Camera.LookAt = lookAt;
        return true;
    }

    /// <summary>设置默认混合时长 不修改宿主配置的逐镜头混合规则</summary>
    /// <param name="seconds">非负有限秒数</param>
    /// <returns>是否接受有效设置</returns>
    public bool SetDefaultBlendDuration(float seconds)
    {
        if (!IsReady || !IsFinite(seconds) || seconds < 0f) return false;
        _host.Brain.m_DefaultBlend = new CinemachineBlendDefinition(
            seconds == 0f ? CinemachineBlendDefinition.Style.Cut : CinemachineBlendDefinition.Style.EaseInOut, seconds);
        return true;
    }

    /// <summary>根据宿主震动源的形状和频道生成信号 拒绝未配置的旧版信号源</summary>
    /// <param name="strength">正数强度倍率</param>
    /// <returns>是否生成震动信号</returns>
    public bool Shake(float strength = 1f)
    {
        if (!IsReady || !IsFinite(strength) || strength <= 0f) return false;
        var source = _host.ImpulseSource;
        if (source == null || !source.isActiveAndEnabled || source.m_ImpulseDefinition == null) return false;
        var definition = source.m_ImpulseDefinition;
        if (definition.m_ImpulseType == CinemachineImpulseDefinition.ImpulseTypes.Legacy && definition.m_RawSignal == null)
            return false;
        source.GenerateImpulseWithForce(strength);
        return true;
    }

    /// <summary>检查标识 组件重复 顶层归属和输出层遮罩 不修改任何对象</summary>
    /// <param name="cameras">需要检查重复的镜头集合</param>
    /// <param name="host">目标宿主</param>
    /// <param name="id">镜头标识</param>
    /// <param name="camera">镜头组件</param>
    private static void ValidateRegistration(Dictionary<string, CameraState> cameras, CameraServiceHost host,
        string id, CinemachineVirtualCameraBase camera)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("镜头标识不能为空", nameof(id));
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        if (cameras.ContainsKey(id)) throw new InvalidOperationException($"镜头标识已注册:{id}");
        foreach (var state in cameras.Values)
            if (state.Camera == camera) throw new InvalidOperationException($"镜头组件不能重复注册:{id}");
        if (camera.ParentCamera != null) throw new InvalidOperationException($"只能注册顶层镜头 不能单独注册组合镜头的子镜头:{id}");
        if ((host.OutputCamera.cullingMask & (1 << camera.gameObject.layer)) == 0)
            throw new InvalidOperationException($"镜头所在层不在输出摄像机的剔除遮罩中:{id}");
    }

    /// <summary>获取生命周期与宿主均有效的注册镜头</summary>
    /// <param name="id">镜头标识</param>
    /// <param name="state">注册状态</param>
    /// <returns>是否找到有效镜头</returns>
    private bool TryGetCamera(string id, out CameraState state)
    {
        state = null;
        return IsReady && !string.IsNullOrWhiteSpace(id) && _cameras.TryGetValue(id, out state) && state.Camera != null;
    }

    /// <summary>修改优先级后立即更新Cinemachine队列 避免同一帧连续切换仍使用旧排序</summary>
    /// <param name="camera">目标镜头</param>
    /// <param name="priority">新的优先级</param>
    private static void SetPriority(CinemachineVirtualCameraBase camera, int priority)
    {
        camera.Priority = priority;
        camera.MoveToTopOfPrioritySubqueue();
    }

    /// <summary>释放当前绑定并恢复原始配置 Unity已销毁的组件仅清除托管引用</summary>
    private void ReleaseHost()
    {
        if (!ReferenceEquals(_host, null))
        {
            _host.Unavailable -= OnHostUnavailable;
            if (ReferenceEquals(_host.Owner, this)) _host.Owner = null;
        }
        foreach (var state in _cameras.Values) state.Restore();
        _cameras.Clear();
        if (_host != null && _host.Brain != null) _host.Brain.m_DefaultBlend = _originalBlend;
        _host = null;
        _currentCameraId = null;
    }

    /// <summary>在没有有效宿主时绑定主摄像机 避免叠加场景抢占已有输出</summary>
    private void TryBindMainCamera()
    {
        if (IsReady || _disposed || _lifetimeToken.IsCancellationRequested) return;
        var camera = Camera.main;
        if (camera != null && camera.TryGetComponent<CameraServiceHost>(out var host) && host.IsAvailable) BindHost(host);
    }

    /// <summary>场景加载后尝试绑定其主摄像机</summary>
    /// <param name="scene">加载完成的场景</param>
    /// <param name="mode">场景加载方式</param>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryBindMainCamera();

    /// <summary>卸载时移除跨场景注册的失效镜头 并尝试接入剩余场景的宿主</summary>
    /// <param name="scene">卸载完成的场景</param>
    private void OnSceneUnloaded(Scene scene)
    {
        var removedIds = new List<string>();
        foreach (var pair in _cameras)
            if (pair.Value.Camera == null || pair.Value.Camera.gameObject.scene == scene) removedIds.Add(pair.Key);
        foreach (var id in removedIds) UnregisterCamera(id);
        TryBindMainCamera();
    }

    /// <summary>宿主停用时及时归还其镜头状态</summary>
    /// <param name="host">停用的宿主</param>
    private void OnHostUnavailable(CameraServiceHost host) => UnbindHost(host);

    /// <summary>优先恢复默认镜头 否则选择剩余集合中的可用镜头</summary>
    private void SelectFallbackCamera()
    {
        if (!IsReady || SwitchTo(_host.DefaultCameraId)) return;
        foreach (var pair in _cameras)
            if (SwitchTo(pair.Key)) return;
    }

    /// <summary>对需要强约束的注册操作报告生命周期错误</summary>
    private void EnsureStarted()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraService));
        if (!_started) throw new InvalidOperationException("摄像机服务尚未启动");
        _lifetimeToken.ThrowIfCancellationRequested();
    }

    /// <summary>拒绝会污染镜头参数的非有限浮点数</summary>
    /// <param name="value">待检查数值</param>
    /// <returns>是否为有限值</returns>
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>镜头借用期间的原始优先级与目标快照</summary>
    private sealed class CameraState
    {
        /// <summary>被借用的镜头组件</summary>
        internal CinemachineVirtualCameraBase Camera { get; }
        /// <summary>接管前的优先级</summary>
        private readonly int _priority;
        /// <summary>接管前的跟随目标</summary>
        private readonly Transform _follow;
        /// <summary>接管前的注视目标</summary>
        private readonly Transform _lookAt;

        /// <summary>保存镜头原始配置</summary>
        /// <param name="camera">需要接管的镜头</param>
        internal CameraState(CinemachineVirtualCameraBase camera)
        {
            Camera = camera;
            _priority = camera.Priority;
            _follow = camera.Follow;
            _lookAt = camera.LookAt;
        }

        /// <summary>恢复仍存活的镜头 并清除已销毁目标对应的Unity空引用</summary>
        internal void Restore()
        {
            if (Camera == null) return;
            SetPriority(Camera, _priority);
            Camera.Follow = _follow != null ? _follow : null;
            Camera.LookAt = _lookAt != null ? _lookAt : null;
        }
    }
}
