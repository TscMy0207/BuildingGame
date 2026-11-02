using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using NUnit.Framework;
using VContainer;
using VContainer.Unity;

/// <summary>验证自动发现 依赖注入 共享实例和容器释放行为</summary>
public sealed class CoreCompositionTests
{
    /// <summary>通过真实程序集扫描验证间接继承和构造函数注入</summary>
    [Test]
    public void DiscoversConcreteDescendantsAndSharesContracts()
    {
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(CoreCompositionTests).Assembly.GetTypes());
        using var container = builder.Build();
        var system = container.Resolve<ProbeSystem>();
        Assert.That(container.Resolve<IProbeSystem>(), Is.SameAs(system));
        Assert.That(container.Resolve<IProbeModel>(), Is.SameAs(system.Model));
        Assert.That(container.Resolve<ProbeModel>(), Is.SameAs(system.Model));
        using var child = container.CreateScope();
        Assert.That(child.Resolve<ProbeSystem>(), Is.SameAs(system));
        Assert.That(child.Resolve<ProbeModel>(), Is.SameAs(system.Model));
    }

    /// <summary>容器按依赖逆序释放同一实例且重复释放不会再次执行钩子</summary>
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

    /// <summary>注册阶段不执行构造函数 未被使用的实例也不会被创建</summary>
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

    /// <summary>默认只发现Game程序集 不会注册测试或其他程序集的业务类型</summary>
    [Test]
    public void DefaultDiscoveryIsLimitedToGameAssembly()
    {
        var builder = new ContainerBuilder();
        CoreComposition.RegisterDefaults(builder);
        Assert.That(builder.Exists(typeof(ProbeSystem)), Is.False);
        Assert.That(builder.Exists(typeof(ProbeModel)), Is.False);
    }

    /// <summary>忽略基类 抽象类 开放泛型及普通类型 并对重复输入去重</summary>
    [Test]
    public void FiltersUnsupportedTypesAndDeduplicatesCandidates()
    {
        var builder = new ContainerBuilder();
        RegisterTypes(builder, typeof(ModuleSystem), typeof(ModuleModel), typeof(AbstractModel),
            typeof(DuplicateModel<>), typeof(string), null, typeof(ProbeModel), typeof(ProbeModel));
        Assert.That(builder.Count, Is.EqualTo(1));
        Assert.That(builder.Exists(typeof(AbstractModel)), Is.False);
        Assert.That(builder.Exists(typeof(DuplicateModel<>)), Is.False);
        Assert.That(builder.Exists(typeof(IDisposable), true), Is.False);
    }

    /// <summary>业务接口冲突时在写入任何注册前明确失败</summary>
    [Test]
    public void RejectsDuplicateContractsWithoutPartialRegistration()
    {
        var builder = new ContainerBuilder();
        var error = Assert.Throws<InvalidOperationException>(() =>
            RegisterTypes(builder, typeof(ProbeModel), typeof(DuplicateModel<int>)));
        StringAssert.Contains(typeof(IProbeModel).FullName, error.Message);
        Assert.That(builder.Count, Is.Zero);
    }

    /// <summary>已有接口注册不能被自动发现结果覆盖</summary>
    [Test]
    public void RejectsExistingContractWithoutChangingBuilder()
    {
        var builder = new ContainerBuilder();
        builder.Register<ProbeModel>(Lifetime.Singleton).As<IProbeModel>();
        Assert.Throws<InvalidOperationException>(() => RegisterTypes(builder, typeof(DuplicateModel<int>)));
        Assert.That(builder.Count, Is.EqualTo(1));
    }

    /// <summary>已有自身注册不能再被自动登记</summary>
    [Test]
    public void RejectsExistingConcreteRegistration()
    {
        var builder = new ContainerBuilder();
        builder.Register<ProbeModel>(Lifetime.Singleton);
        Assert.Throws<InvalidOperationException>(() => RegisterTypes(builder, typeof(ProbeModel)));
        Assert.That(builder.Count, Is.EqualTo(1));
    }

    /// <summary>继承基类即可释放 不强制实现清理钩子</summary>
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

    /// <summary>清理钩子抛出异常后依然保持已释放状态且不重复执行</summary>
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

    /// <summary>拒绝缺失的容器参数</summary>
    [Test]
    public void RejectsNullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => CoreComposition.RegisterDefaults(null));
    }

    /// <summary>反射发现的嵌套具体类型被精确保留 抽象类型与开放泛型不写入清单</summary>
    [Test]
    public void LinkerRulesPreserveDiscoveredTypesAndMembers()
    {
        var document = (XDocument)typeof(CoreModuleLinkerProcessor)
            .GetMethod("CreateLinkerDocument", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { typeof(CoreCompositionTests).Assembly });
        var assembly = document.Root.Element("assembly");
        Assert.That((string)assembly.Attribute("fullname"), Is.EqualTo(typeof(CoreCompositionTests).Assembly.GetName().Name));
        var entries = assembly.Elements("type").ToArray();
        Assert.That(entries.Select(entry => (string)entry.Attribute("fullname")),
            Does.Contain(typeof(ProbeSystem).FullName.Replace('+', '/'))
                .And.Contain(typeof(ProbeModel).FullName.Replace('+', '/'))
                .And.Not.Contain(typeof(AbstractModel).FullName.Replace('+', '/'))
                .And.Not.Contain(typeof(DuplicateModel<>).FullName.Replace('+', '/'))
                .And.Not.Contain(typeof(CoreCompositionTests).FullName));
        Assert.That(entries.All(entry => (string)entry.Attribute("preserve") == "all"), Is.True);
    }

    /// <summary>通过非公开测试入口传入候选类型 不扩大生产环境的扫描范围</summary>
    /// <param name="builder">待验证的容器</param>
    /// <param name="types">模拟程序集发现的类型</param>
    private static void RegisterTypes(IContainerBuilder builder, params Type[] types)
    {
        try
        {
            typeof(CoreComposition).GetMethod("RegisterTypes", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { builder, types });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    /// <summary>测试业务数据访问契约</summary>
    public interface IProbeModel { }

    /// <summary>测试业务系统访问契约</summary>
    public interface IProbeSystem { }

    /// <summary>验证间接继承识别的抽象数据基类</summary>
    public abstract class AbstractModel : ModuleModel { }

    /// <summary>记录真实容器释放结果的业务数据</summary>
    public sealed class ProbeModel : AbstractModel, IProbeModel
    {
        /// <summary>清理执行次数</summary>
        public int DisposeCount { get; private set; }
        /// <summary>记录数据释放</summary>
        protected override void OnDispose() => DisposeCount++;
    }

    /// <summary>验证依赖注入与逆序释放的业务系统</summary>
    public sealed class ProbeSystem : ModuleSystem, IProbeSystem
    {
        /// <summary>构造函数注入的业务数据</summary>
        public IProbeModel Model { get; }
        /// <summary>清理执行次数</summary>
        public int DisposeCount { get; private set; }
        /// <summary>清理系统时数据是否仍然有效</summary>
        public bool ModelWasAliveOnDispose { get; private set; }
        /// <summary>接收由容器创建的数据实例</summary>
        /// <param name="model">业务数据</param>
        public ProbeSystem(IProbeModel model) => Model = model;
        /// <summary>验证依赖对象的存活状态并覆盖重入释放边界</summary>
        protected override void OnDispose()
        {
            DisposeCount++;
            ModelWasAliveOnDispose = !((ProbeModel)Model).IsDisposed;
            Dispose();
        }
    }

    /// <summary>通过开放泛型避免自动发现时产生冲突 闭合后用于验证重复契约</summary>
    /// <typeparam name="T">测试类型参数</typeparam>
    public sealed class DuplicateModel<T> : ModuleModel, IProbeModel { }

    /// <summary>验证注册与实例化的分离</summary>
    public sealed class UnusedSystem : ModuleSystem
    {
        /// <summary>累计构造次数</summary>
        public static int ConstructionCount { get; set; }
        /// <summary>记录实际实例化时机</summary>
        public UnusedSystem() => ConstructionCount++;
    }

    /// <summary>模拟系统清理失败</summary>
    public sealed class ThrowingSystem : ModuleSystem
    {
        /// <summary>向调用方报告测试清理异常</summary>
        protected override void OnDispose() => throw new InvalidOperationException("测试系统清理失败");
    }

    /// <summary>模拟数据清理失败</summary>
    public sealed class ThrowingModel : ModuleModel
    {
        /// <summary>向调用方报告测试清理异常</summary>
        protected override void OnDispose() => throw new InvalidOperationException("测试数据清理失败");
    }
}
