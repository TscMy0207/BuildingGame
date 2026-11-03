using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 验证自动注册的发现 校验 共享实例和容器释放行为
/// </summary>
public class AutoRegistrationTests
{
    /// <summary>
    /// 通过真实扫描验证特性声明 接口共享和子容器解析
    /// </summary>
    [Test]
    public void DiscoversAttributedTypesAndSharesContracts()
    {
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(ProbeSystem), typeof(ProbeModel));
        using var container = builder.Build();
        var system = container.Resolve<ProbeSystem>();
        Assert.That(container.Resolve<IProbeSystem>(), Is.SameAs(system));
        Assert.That(container.Resolve<IProbeModel>(), Is.SameAs(system.Model));
        Assert.That(container.Resolve<ProbeModel>(), Is.SameAs(system.Model));
        using var child = container.CreateScope();
        Assert.That(child.Resolve<ProbeSystem>(), Is.SameAs(system));
        Assert.That(child.Resolve<ProbeModel>(), Is.SameAs(system.Model));
    }

    /// <summary>
    /// 容器按依赖逆序释放同一实例且重复释放不会再次执行钩子
    /// </summary>
    [Test]
    public void ContainerDisposesSystemBeforeItsModelExactlyOnce()
    {
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(ProbeModel), typeof(ProbeSystem));
        var container = builder.Build();
        var system = container.Resolve<ProbeSystem>();
        var model = container.Resolve<ProbeModel>();
        container.Resolve<IProbeSystem>();
        container.Resolve<IProbeModel>();
        container.Dispose();
        container.Dispose();
        system.Dispose();
        model.Dispose();
        Assert.That(system.IsDisposed, Is.True);
        Assert.That(model.IsDisposed, Is.True);
        Assert.That(system.DisposeCount, Is.EqualTo(1));
        Assert.That(model.DisposeCount, Is.EqualTo(1));
        Assert.That(system.ModelWasAliveOnDispose, Is.True);
    }

    /// <summary>
    /// 注册阶段不执行构造函数 未被使用的实例也不会被创建
    /// </summary>
    [Test]
    public void RegistrationDoesNotConstructUnusedModules()
    {
        UnusedSystem.ConstructionCount = 0;
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(UnusedSystem));
        using var container = builder.Build();
        Assert.That(UnusedSystem.ConstructionCount, Is.Zero);
        container.Resolve<UnusedSystem>();
        Assert.That(UnusedSystem.ConstructionCount, Is.EqualTo(1));
    }

    /// <summary>
    /// 默认只发现Game程序集 不会注册测试或其他程序集的业务类型
    /// </summary>
    [Test]
    public void DefaultDiscoveryIsLimitedToGameAssembly()
    {
        var builder = new ContainerBuilder();
        GameComposition.RegisterDefaults(builder);
        Assert.That(builder.Exists(typeof(ProbeSystem)), Is.False);
        Assert.That(builder.Exists(typeof(ProbeModel)), Is.False);
    }

    /// <summary>
    /// 关闭自身暴露后只保留显式声明的接口
    /// </summary>
    [Test]
    public void AsSelfFalseKeepsConcreteTypeUnregistered()
    {
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(ContractOnlySystem));
        using var container = builder.Build();
        Assert.That(container.Resolve<IContractOnlySystem>(), Is.InstanceOf<ContractOnlySystem>());
        Assert.Throws<VContainerException>(() => container.Resolve<ContractOnlySystem>());
    }

    /// <summary>
    /// 拒绝缺少标记接口 抽象类 开放泛型和Unity对象
    /// </summary>
    [Test]
    public void RejectsInvalidTypeLevelDeclarations()
    {
        AssertRejected(typeof(PlainType), new AutoRegisterAttribute(), "ISystem或IModel");
        AssertRejected(typeof(AbstractSystem), new AutoRegisterAttribute(), "抽象类");
        AssertRejected(typeof(OpenGenericSystem<>), new AutoRegisterAttribute(), "开放泛型");
        AssertRejected(typeof(GameObject), new AutoRegisterAttribute(), "UnityEngine.Object");
    }

    /// <summary>
    /// 拒绝非接口 标记接口 未实现接口和空契约声明
    /// </summary>
    [Test]
    public void RejectsInvalidContractDeclarations()
    {
        AssertRejected(typeof(BadContractSystem), new AutoRegisterAttribute(typeof(string)), "必须是接口");
        AssertRejected(typeof(BadContractSystem), new AutoRegisterAttribute(typeof(IDisposable)), "禁止暴露");
        AssertRejected(typeof(BadContractSystem), new AutoRegisterAttribute(typeof(IUnimplemented)), "未实现契约");
        AssertRejected(typeof(BadContractSystem), new AutoRegisterAttribute { AsSelf = false }, "必须声明契约");
    }

    /// <summary>
    /// 业务接口冲突时在写入任何注册前明确失败
    /// </summary>
    [Test]
    public void RejectsDuplicateContractsWithoutPartialRegistration()
    {
        var builder = new ContainerBuilder();
        var error = Assert.Throws<InvalidOperationException>(() =>
            RegisterTypes(builder, typeof(ProbeModel), typeof(DuplicateProbeModel)));
        StringAssert.Contains(typeof(IProbeModel).FullName, error.Message);
        Assert.That(builder.Count, Is.Zero);
    }

    /// <summary>
    /// 已有接口注册不能被自动发现结果覆盖
    /// </summary>
    [Test]
    public void RejectsExistingContractWithoutChangingBuilder()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance<IProbeModel>(new ProbeModel());
        Assert.Throws<InvalidOperationException>(() => RegisterTypes(builder, typeof(ProbeModel)));
        Assert.That(builder.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// 已有自身注册不能再被自动登记
    /// </summary>
    [Test]
    public void RejectsExistingConcreteRegistration()
    {
        var builder = new ContainerBuilder();
        builder.Register<ProbeModel>(Lifetime.Singleton);
        Assert.Throws<InvalidOperationException>(() => RegisterTypes(builder, typeof(ProbeModel)));
        Assert.That(builder.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// 集合契约允许多个实现并全部保留
    /// </summary>
    [Test]
    public void AllowsMultipleImplementationsForCollectionContracts()
    {
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(FirstSubService), typeof(SecondSubService));
        using var container = builder.Build();
        var services = container.Resolve<IEnumerable<IConfigTableSubService>>().ToArray();
        Assert.That(services.Length, Is.EqualTo(2));
        Assert.That(services, Has.Exactly(1).InstanceOf<FirstSubService>());
        Assert.That(services, Has.Exactly(1).InstanceOf<SecondSubService>());
    }

    /// <summary>
    /// 单值契约存在多个实现时拒绝注册
    /// </summary>
    [Test]
    public void RejectsMultipleImplementationsForSingleContracts()
    {
        var builder = new ContainerBuilder();
        var error = Assert.Throws<InvalidOperationException>(() =>
            RegisterTypes(builder, typeof(ProbeSystem), typeof(SecondProbeSystem)));
        StringAssert.Contains(typeof(IProbeSystem).FullName, error.Message);
        Assert.That(builder.Count, Is.Zero);
    }

    /// <summary>
    /// 继承基类即可释放 不强制实现清理钩子
    /// </summary>
    [Test]
    public void EmptyCleanupHooksAreOptional()
    {
        var system = new UnusedSystem();
        var model = new DuplicateModel<int>();
        system.Dispose();
        model.Dispose();
        Assert.That(system.IsDisposed, Is.True);
        Assert.That(model.IsDisposed, Is.True);
    }

    /// <summary>
    /// 清理钩子抛出异常后依然保持已释放状态且不重复执行
    /// </summary>
    /// <param name="system">是否验证系统基类</param>
    [TestCase(true)]
    [TestCase(false)]
    public void CleanupFailureDoesNotExecuteHookAgain(bool system)
    {
        IDisposable module = system ? (IDisposable)new ThrowingSystem() : new ThrowingModel();
        Assert.Throws<InvalidOperationException>(() => module.Dispose());
        Assert.DoesNotThrow(() => module.Dispose());
        Assert.That(system ? ((ThrowingSystem)module).IsDisposed : ((ThrowingModel)module).IsDisposed, Is.True);
    }

    /// <summary>
    /// 拒绝缺失的容器参数
    /// </summary>
    [Test]
    public void RejectsNullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => GameComposition.RegisterDefaults(null));
        Assert.Throws<ArgumentNullException>(() => GameComposition.GetAutoRegisteredTypes(null));
    }

    /// <summary>
    /// 自动注册类型被精确保留 未参与的类型不写入清单
    /// </summary>
    [Test]
    public void LinkerRulesPreserveDiscoveredTypesAndMembers()
    {
        var document = (XDocument)typeof(AutoRegistrationLinkerProcessor)
            .GetMethod("CreateLinkerDocument", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { new[] { typeof(ProbeSystem), typeof(ProbeModel) }, "TestAssembly" });
        var assembly = document.Root.Element("assembly");
        Assert.That((string)assembly.Attribute("fullname"), Is.EqualTo("TestAssembly"));
        var entries = assembly.Elements("type").ToArray();
        Assert.That(entries.Select(entry => (string)entry.Attribute("fullname")),
            Does.Contain(typeof(ProbeSystem).FullName.Replace('+', '/'))
                .And.Contain(typeof(ProbeModel).FullName.Replace('+', '/'))
                .And.Not.Contain(typeof(PlainType).FullName.Replace('+', '/')));
        Assert.That(entries.All(entry => (string)entry.Attribute("preserve") == "all"), Is.True);
    }

    /// <summary>
    /// 通过非公开测试入口传入候选类型 不扩大生产环境的扫描范围
    /// </summary>
    /// <param name="builder">待验证的容器</param>
    /// <param name="types">模拟程序集发现的类型</param>
    private static void RegisterTypes(IContainerBuilder builder, params Type[] types)
    {
        try
        {
            AutoRegistrationType.GetMethod("RegisterTypes", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { builder, types });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    /// <summary>
    /// 通过非公开测试入口校验单个类型声明
    /// </summary>
    /// <param name="type">待校验类型</param>
    /// <param name="attribute">自动注册声明</param>
    /// <param name="error">收集到的错误</param>
    /// <returns>是否通过校验</returns>
    private static bool Validate(Type type, AutoRegisterAttribute attribute, out string error)
    {
        var errors = new List<string>();
        var valid = (bool)AutoRegistrationType.GetMethod("ValidateType", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { type, attribute, errors });
        error = string.Join(Environment.NewLine, errors);
        return valid;
    }

    /// <summary>
    /// 断言类型声明被拒绝并包含指定原因
    /// </summary>
    /// <param name="type">待校验类型</param>
    /// <param name="attribute">自动注册声明</param>
    /// <param name="reason">期望的错误片段</param>
    private static void AssertRejected(Type type, AutoRegisterAttribute attribute, string reason)
    {
        Assert.That(Validate(type, attribute, out var error), Is.False);
        StringAssert.Contains(reason, error);
    }

    /// <summary>
    /// 运行时自动注册器类型
    /// </summary>
    private static Type AutoRegistrationType => typeof(GameComposition).Assembly.GetType("AutoRegistration", true);

    /// <summary>
    /// 测试业务数据访问契约
    /// </summary>
    public interface IProbeModel { }

    /// <summary>
    /// 测试业务系统访问契约
    /// </summary>
    public interface IProbeSystem { }

    /// <summary>
    /// 仅通过接口消费的测试契约
    /// </summary>
    public interface IContractOnlySystem { }

    /// <summary>
    /// 用于验证未实现契约的测试接口
    /// </summary>
    public interface IUnimplemented { }

    /// <summary>
    /// 记录真实容器释放结果的业务数据
    /// </summary>
    [AutoRegister(typeof(IProbeModel))]
    public class ProbeModel : ModuleModel, IProbeModel
    {
        /// <summary>
        /// 清理执行次数
        /// </summary>
        public int DisposeCount { get; private set; }
        /// <summary>
        /// 记录数据释放
        /// </summary>
        protected override void OnDispose() => DisposeCount++;
    }

    /// <summary>
    /// 验证依赖注入与逆序释放的业务系统
    /// </summary>
    [AutoRegister(typeof(IProbeSystem))]
    public class ProbeSystem : ModuleSystem, IProbeSystem
    {
        /// <summary>
        /// 构造函数注入的业务数据
        /// </summary>
        public IProbeModel Model { get; }
        /// <summary>
        /// 清理执行次数
        /// </summary>
        public int DisposeCount { get; private set; }
        /// <summary>
        /// 清理系统时数据是否仍然有效
        /// </summary>
        public bool ModelWasAliveOnDispose { get; private set; }

        /// <summary>
        /// 接收由容器创建的数据实例
        /// </summary>
        /// <param name="model">业务数据</param>
        public ProbeSystem(IProbeModel model) => Model = model;

        /// <summary>
        /// 验证依赖对象的存活状态并覆盖重入释放边界
        /// </summary>
        protected override void OnDispose()
        {
            DisposeCount++;
            ModelWasAliveOnDispose = !((ProbeModel)Model).IsDisposed;
            Dispose();
        }
    }

    /// <summary>
    /// 提供重复接口契约的业务数据
    /// </summary>
    [AutoRegister(typeof(IProbeModel))]
    public class DuplicateProbeModel : ModuleModel, IProbeModel { }

    /// <summary>
    /// 提供重复接口契约的第二个业务系统
    /// </summary>
    [AutoRegister(typeof(IProbeSystem), AsSelf = false)]
    public class SecondProbeSystem : ModuleSystem, IProbeSystem { }

    /// <summary>
    /// 仅暴露接口的业务系统
    /// </summary>
    [AutoRegister(typeof(IContractOnlySystem), AsSelf = false)]
    public class ContractOnlySystem : ModuleSystem, IContractOnlySystem { }

    /// <summary>
    /// 验证注册与实例化的分离
    /// </summary>
    [AutoRegister]
    public class UnusedSystem : ModuleSystem
    {
        /// <summary>
        /// 累计构造次数
        /// </summary>
        public static int ConstructionCount { get; set; }

        /// <summary>
        /// 记录实际实例化时机
        /// </summary>
        public UnusedSystem() => ConstructionCount++;
    }

    /// <summary>
    /// 集合契约的第一个测试实现
    /// </summary>
    [AutoRegister(typeof(IConfigTableSubService), AsSelf = false)]
    public class FirstSubService : ModuleSystem, IConfigTableSubService
    {
        /// <summary>
        /// 配置集合名称
        /// </summary>
        public string Name => "First";
        /// <summary>
        /// 完成加载
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>已完成任务</returns>
        public UniTask LoadAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
        /// <summary>
        /// 清除配置引用
        /// </summary>
        public void Clear() { }
    }

    /// <summary>
    /// 集合契约的第二个测试实现
    /// </summary>
    [AutoRegister(typeof(IConfigTableSubService), AsSelf = false)]
    public class SecondSubService : ModuleSystem, IConfigTableSubService
    {
        /// <summary>
        /// 配置集合名称
        /// </summary>
        public string Name => "Second";
        /// <summary>
        /// 完成加载
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>已完成任务</returns>
        public UniTask LoadAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
        /// <summary>
        /// 清除配置引用
        /// </summary>
        public void Clear() { }
    }

    /// <summary>
    /// 验证间接继承识别的抽象数据基类
    /// </summary>
    public abstract class AbstractSystem : ModuleSystem { }

    /// <summary>
    /// 验证开放泛型拒绝规则
    /// </summary>
    /// <typeparam name="T">测试类型参数</typeparam>
    public class OpenGenericSystem<T> : ModuleSystem { }

    /// <summary>
    /// 未标记的普通类型
    /// </summary>
    public class PlainType { }

    /// <summary>
    /// 验证非法契约声明的业务系统
    /// </summary>
    public class BadContractSystem : ModuleSystem { }

    /// <summary>
    /// 通过开放泛型避免自动发现时产生冲突 闭合后用于验证重复契约
    /// </summary>
    /// <typeparam name="T">测试类型参数</typeparam>
    public class DuplicateModel<T> : ModuleModel, IProbeModel { }

    /// <summary>
    /// 模拟系统清理失败
    /// </summary>
    public class ThrowingSystem : ModuleSystem
    {
        /// <summary>
        /// 向调用方报告测试清理异常
        /// </summary>
        protected override void OnDispose() => throw new InvalidOperationException("测试系统清理失败");
    }

    /// <summary>
    /// 模拟数据清理失败
    /// </summary>
    public class ThrowingModel : ModuleModel
    {
        /// <summary>
        /// 向调用方报告测试清理异常
        /// </summary>
        protected override void OnDispose() => throw new InvalidOperationException("测试数据清理失败");
    }
}
