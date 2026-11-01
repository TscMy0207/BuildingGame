using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cinemachine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>通过实际Cinemachine组件验证镜头输出 混合 震动及生命周期</summary>
public sealed class CameraServiceTests
{
    /// <summary>测试持有的框架容器</summary>
    private IObjectResolver _container;
    /// <summary>容器提供的真实摄像机服务</summary>
    private ICameraService _service;
    /// <summary>借用的测试场景宿主</summary>
    private CameraServiceHost _host;
    /// <summary>使用手动更新以便验证最终输出的镜头驱动器</summary>
    private CinemachineBrain _brain;
    /// <summary>第一个已注册镜头</summary>
    private CinemachineVirtualCamera _first;
    /// <summary>第二个已注册镜头</summary>
    private CinemachineVirtualCamera _second;
    /// <summary>测试创建的对象 结束时统一销毁</summary>
    private readonly List<GameObject> _objects = new();

    /// <summary>只启动摄像机服务 避免依赖尚未配置的UI与业务启动入口</summary>
    [SetUp]
    public void SetUp()
    {
        var builder = new ContainerBuilder();
        FrameworkComposition.RegisterDefaults(builder);
        _container = builder.Build();
        _service = _container.Resolve<ICameraService>();
        ((IStartupService)_service).StartAsync(new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
        _host = CreateHost("CameraTestHost");
        _brain = _host.GetComponent<CinemachineBrain>();
        _brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.ManualUpdate;
        _brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
        _service.BindHost(_host);
        _first = CreateCamera("First", Vector3.zero);
        _second = CreateCamera("Second", new Vector3(10f, 0f, 0f));
        _service.RegisterCamera("First", _first);
        _service.RegisterCamera("Second", _second);
        CinemachineImpulseManager.Instance.Clear();
    }

    /// <summary>先归还借用状态再销毁对象 防止污染其他测试和场景</summary>
    /// <returns>等待Unity延迟销毁完成</returns>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _container?.Dispose();
        foreach (var instance in _objects)
            if (instance != null) Object.Destroy(instance);
        _objects.Clear();
        CinemachineImpulseManager.Instance.Clear();
        yield return null;
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>验证切换真正改变Brain输出 且非零时长产生并完成混合</summary>
    /// <returns>逐帧驱动真实镜头混合</returns>
    [UnityTest]
    public IEnumerator SwitchingChangesOutputAndCompletesBlend()
    {
        Assert.That(_service.SwitchTo("First"), Is.True);
        yield return null;
        _brain.ManualUpdate();
        Assert.That(_brain.ActiveVirtualCamera, Is.SameAs(_first));
        Assert.That(Vector3.Distance(_service.OutputCamera.transform.position, _first.transform.position), Is.LessThan(0.001f));

        Assert.That(_service.SetDefaultBlendDuration(0.25f), Is.True);
        Assert.That(_service.SwitchTo("Second"), Is.True);
        yield return null;
        _brain.ManualUpdate();
        Assert.That(_brain.IsBlending, Is.True);
        var deadline = Time.realtimeSinceStartup + 2f;
        while (_brain.IsBlending && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            _brain.ManualUpdate();
        }
        Assert.That(_brain.IsBlending, Is.False);
        Assert.That(_brain.ActiveVirtualCamera, Is.SameAs(_second));
        Assert.That(Vector3.Distance(_service.OutputCamera.transform.position, _second.transform.position), Is.LessThan(0.001f));
    }

    /// <summary>验证跟随目标通过Transposer影响真实输出 释放后清除运行期目标并恢复原配置</summary>
    /// <returns>等待跟随管线更新</returns>
    [UnityTest]
    public IEnumerator TargetsDriveOutputAndDisposalRestoresBorrowedState()
    {
        var target = CreateObject("CameraTestTarget").transform;
        target.position = new Vector3(4f, 2f, 3f);
        var body = _first.AddCinemachineComponent<CinemachineTransposer>();
        body.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
        body.m_FollowOffset = new Vector3(0f, 1f, -5f);
        body.m_XDamping = body.m_YDamping = body.m_ZDamping = 0f;
        Assert.That(_service.SetTargets("First", target, target), Is.True);
        Assert.That(_service.SwitchTo("First"), Is.True);
        yield return null;
        _brain.ManualUpdate();
        Assert.That(Vector3.Distance(_service.OutputCamera.transform.position, target.position + body.m_FollowOffset), Is.LessThan(0.001f));
        _service.SetDefaultBlendDuration(0.8f);
        ((IStartupService)_service).Dispose();
        ((IStartupService)_service).Dispose();
        Assert.That(_service.IsReady, Is.False);
        Assert.That(_service.OutputCamera, Is.Null);
        Assert.That(_first.Follow, Is.Null);
        Assert.That(_first.LookAt, Is.Null);
        Assert.That(_first.Priority, Is.EqualTo(7));
        Assert.That(_brain.m_DefaultBlend.m_Style, Is.EqualTo(CinemachineBlendDefinition.Style.Cut));
        Assert.That(_host != null, Is.True, "服务不能销毁借用的场景对象");
        Assert.That(_service.SwitchTo("Second"), Is.False);
        Assert.Throws<ObjectDisposedException>(() => _service.BindHost(_host));
    }

    /// <summary>验证重复注册及无效参数不会改变当前镜头 停用镜头不能被请求切换</summary>
    [Test]
    public void InvalidRequestsPreserveCurrentSelection()
    {
        _service.SwitchTo("First");
        Assert.Throws<InvalidOperationException>(() => _service.RegisterCamera("First", _second));
        Assert.Throws<InvalidOperationException>(() => _service.RegisterCamera("Other", _first));
        Assert.Throws<ArgumentException>(() => _service.RegisterCamera(" ", _second));
        Assert.That(_service.SwitchTo("Missing"), Is.False);
        _second.enabled = false;
        Assert.That(_service.SwitchTo("Second"), Is.False);
        Assert.That(_service.SetDefaultBlendDuration(float.NaN), Is.False);
        Assert.That(_service.SetDefaultBlendDuration(-1f), Is.False);
        Assert.That(_service.Shake(float.PositiveInfinity), Is.False);
        Assert.That(_service.CurrentCameraId, Is.EqualTo("First"));
        Assert.That(_service.UnregisterCamera("First"), Is.True);
        Assert.That(_first.Priority, Is.EqualTo(7));
        Assert.That(_service.UnregisterCamera("First"), Is.False);
    }

    /// <summary>验证宿主停用时即时解绑 新宿主不会被旧宿主的解绑请求影响</summary>
    [Test]
    public void HostDisableAndReplacementReleaseReferences()
    {
        _service.SwitchTo("First");
        _host.enabled = false;
        Assert.That(_service.IsReady, Is.False);
        Assert.That(_service.CurrentCameraId, Is.Null);
        Assert.That(_first.Priority, Is.EqualTo(7));
        var replacement = CreateHost("ReplacementCameraHost");
        _service.BindHost(replacement);
        _service.UnbindHost(_host);
        Assert.That(_service.IsReady, Is.True);
        Assert.That(_service.OutputCamera, Is.SameAs(replacement.OutputCamera));
    }

    /// <summary>验证重复容器不能接管已有宿主 失败者释放后不会影响原服务</summary>
    [Test]
    public void DuplicateServiceCannotTakeAnOwnedHost()
    {
        var builder = new ContainerBuilder();
        FrameworkComposition.RegisterDefaults(builder);
        using var duplicateContainer = builder.Build();
        var duplicate = duplicateContainer.Resolve<ICameraService>();
        ((IStartupService)duplicate).StartAsync(new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
        Assert.Throws<InvalidOperationException>(() => duplicate.BindHost(_host));
        ((IStartupService)duplicate).Dispose();
        Assert.That(_service.IsReady, Is.True);
        Assert.That(_service.SwitchTo("First"), Is.True);
    }

    /// <summary>验证卸载动态镜头所在场景后自动移除失效引用并选择存活镜头</summary>
    /// <returns>等待叠加场景卸载</returns>
    [UnityTest]
    public IEnumerator SceneUnloadRemovesDynamicCameraAndSelectsFallback()
    {
        var scene = SceneManager.CreateScene("CameraServiceUnloadTest");
        var remote = CreateCamera("Remote", Vector3.one);
        SceneManager.MoveGameObjectToScene(remote.gameObject, scene);
        _service.RegisterCamera("Remote", remote);
        _service.SwitchTo("Remote");
        yield return SceneManager.UnloadSceneAsync(scene);
        Assert.That(_service.SwitchTo("Remote"), Is.False);
        Assert.That(_service.UnregisterCamera("Remote"), Is.False);
        Assert.That(_service.CurrentCameraId, Is.EqualTo("First"));
        Assert.That(_service.IsReady, Is.True);
    }

    /// <summary>验证震动源经过真实ImpulseListener影响镜头输出</summary>
    /// <returns>逐帧采样实际震动</returns>
    [UnityTest]
    public IEnumerator ShakeProducesImpulseAndMovesOutput()
    {
        var source = _host.GetComponent<CinemachineImpulseSource>();
        source.m_ImpulseDefinition.m_ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        source.m_ImpulseDefinition.m_ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        source.m_ImpulseDefinition.m_ImpulseDuration = 0.5f;
        source.m_DefaultVelocity = Vector3.down;
        var listener = _first.gameObject.AddComponent<CinemachineImpulseListener>();
        listener.m_ChannelMask = 1;
        listener.m_Gain = 1f;
        _service.SwitchTo("First");
        yield return null;
        _brain.ManualUpdate();
        var originalPosition = _service.OutputCamera.transform.position;
        Assert.That(_service.Shake(), Is.True);
        var moved = false;
        for (var frame = 0; frame < 10; frame++)
        {
            yield return null;
            _brain.ManualUpdate();
            moved |= Vector3.Distance(_service.OutputCamera.transform.position, originalPosition) > 0.001f;
        }
        Assert.That(moved, Is.True, "震动必须实际影响输出摄像机");
    }

    /// <summary>创建由测试持有的临时对象</summary>
    /// <param name="name">对象名称</param>
    /// <returns>临时对象</returns>
    private GameObject CreateObject(string name)
    {
        var instance = new GameObject(name);
        _objects.Add(instance);
        return instance;
    }

    /// <summary>创建带有输出组件的场景宿主</summary>
    /// <param name="name">对象名称</param>
    /// <returns>宿主组件</returns>
    private CameraServiceHost CreateHost(string name) => CreateObject(name).AddComponent<CameraServiceHost>();

    /// <summary>创建具有可观察位置及原始优先级的虚拟镜头</summary>
    /// <param name="name">对象名称</param>
    /// <param name="position">镜头位置</param>
    /// <returns>虚拟镜头组件</returns>
    private CinemachineVirtualCamera CreateCamera(string name, Vector3 position)
    {
        var camera = CreateObject(name).AddComponent<CinemachineVirtualCamera>();
        camera.transform.position = position;
        camera.Priority = 7;
        return camera;
    }
}
