using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>通过真实运行时组件验证View依赖注入和对象释放</summary>
public sealed class CoreModuleInjectionTests
{
    /// <summary>动态实例化的View获得共享业务系统且销毁View不会提前释放系统</summary>
    [UnityTest]
    public IEnumerator InstantiatedViewReceivesContainerOwnedSystem()
    {
        var builder = new ContainerBuilder();
        typeof(CoreComposition).GetMethod("RegisterTypes", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { builder, new[] { typeof(ProbeModel), typeof(ProbeSystem) } });
        using var container = builder.Build();
        var prefab = new GameObject("业务注入测试模板");
        GameObject instance = null;
        try
        {
            prefab.SetActive(false);
            prefab.AddComponent<CoreInjectionProbeView>();
            instance = container.Instantiate(prefab);
            instance.SetActive(true);
            var system = container.Resolve<ProbeSystem>();
            Assert.That(instance.GetComponent<CoreInjectionProbeView>().System, Is.SameAs(system));
            Assert.That(system.Model, Is.SameAs(container.Resolve<ProbeModel>()));
            Object.Destroy(instance);
            yield return null;
            Assert.That(system.IsDisposed, Is.False);
            container.Dispose();
            Assert.That(system.IsDisposed, Is.True);
            Assert.That(((ProbeModel)system.Model).IsDisposed, Is.True);
        }
        finally
        {
            if (instance != null) Object.Destroy(instance);
            Object.Destroy(prefab);
        }
    }

    /// <summary>用于运行时注入的业务系统契约</summary>
    public interface IProbeSystem { }

    /// <summary>运行时业务数据探针</summary>
    public sealed class ProbeModel : ModuleModel { }

    /// <summary>运行时业务系统探针</summary>
    public sealed class ProbeSystem : ModuleSystem, IProbeSystem
    {
        /// <summary>注入的数据实例</summary>
        public ModuleModel Model { get; }
        /// <summary>接收容器创建的业务数据</summary>
        /// <param name="model">业务数据探针</param>
        public ProbeSystem(ProbeModel model) => Model = model;
    }
}
