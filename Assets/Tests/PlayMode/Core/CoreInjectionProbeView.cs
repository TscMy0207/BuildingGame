using UnityEngine;
using VContainer;

/// <summary>验证动态创建的View可以通过注入获得业务系统</summary>
public sealed class CoreInjectionProbeView : MonoBehaviour
{
    /// <summary>注入的业务系统</summary>
    public CoreModuleInjectionTests.IProbeSystem System { get; private set; }

    /// <summary>在容器注入阶段接收共享系统</summary>
    /// <param name="system">容器管理的业务系统</param>
    [Inject]
    public void Construct(CoreModuleInjectionTests.IProbeSystem system) => System = system;
}
