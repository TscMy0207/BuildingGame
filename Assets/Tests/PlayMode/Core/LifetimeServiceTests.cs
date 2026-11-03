using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildingGame.Config.Framework;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using YooAsset;
using Object = UnityEngine.Object;

/// <summary>
/// 验证替代旧Scope后的注入及自动清理所有权
/// </summary>
public class LifetimeServiceTests
{
    /// <summary>
    /// 测试使用的逻辑预制体地址
    /// </summary>
    private const string Location = "Test/Lifetime.prefab";
    /// <summary>
    /// 测试全局依赖容器
    /// </summary>
    private IObjectResolver _container;
    /// <summary>
    /// 全局事件服务
    /// </summary>
    private IEventService _events;
    /// <summary>
    /// 全局定时器服务
    /// </summary>
    private ITimeService _timers;
    /// <summary>
    /// 计数加载器
    /// </summary>
    private Loader _loader;
    /// <summary>
    /// 测试拥有的Unity对象
    /// </summary>
    private readonly List<GameObject> _objects = new();
    /// <summary>
    /// 内存模板
    /// </summary>
    private GameObject _prefab;
    /// <summary>
    /// 事件测试键
    /// </summary>
    private readonly EventKey<int> _key = new(901, "生命周期测试");

    /// <summary>
    /// 配置真实框架服务并注入内存资源
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        var fixture = Own(new GameObject("生命周期测试模板父节点"));
        fixture.SetActive(false);
        _prefab = Own(new GameObject("生命周期测试模板"));
        _prefab.transform.SetParent(fixture.transform);
        _prefab.AddComponent<LifetimePanelProbe>();
        _loader = new Loader(_prefab);
        var builder = new ContainerBuilder();
        GameComposition.RegisterDefaults(builder);
        builder.RegisterInstance<IResourcesService>(_loader);
        builder.RegisterInstance<IFrameworkConfigTableService>(new Config());
        builder.RegisterInstance(new Dependency());
        _container = builder.Build();
        _events = _container.Resolve<IEventService>();
        _timers = _container.Resolve<ITimeService>();
        ((IStartupService)_events).StartAsync(new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
        ((IStartupService)_timers).StartAsync(new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 销毁测试对象并等待清理完成
    /// </summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _loader.Complete();
        foreach (var instance in _objects) if (instance != null) Object.Destroy(instance);
        _container.Dispose();
        yield return null;
        yield return null;
        _objects.Clear();
    }

    /// <summary>
    /// 记录测试对象供失败时清理
    /// </summary>
    private GameObject Own(GameObject instance) { _objects.Add(instance); return instance; }

    /// <summary>
    /// 事件释放集合可复用且已结束订阅不再积累
    /// </summary>
    [Test]
    public void EventBagClearAndDisposeAreIdempotent()
    {
        using var bag = new DisposableBag();
        var calls = 0;
        var subscription = _events.Subscribe(_key, _ => calls++).AddTo(bag);
        _events.Publish(_key, 1);
        subscription.Dispose();
        Assert.That(bag.Count, Is.Zero);
        _events.Publish(_key, 2);
        Assert.That(calls, Is.EqualTo(1));
        var second = _events.Subscribe(_key, _ => calls++).AddTo(bag);
        bag.Clear();
        Assert.That(second.IsDisposed, Is.True);
        var third = _events.Subscribe(_key, _ => calls++).AddTo(bag);
        bag.Dispose();
        Assert.That(third.IsDisposed, Is.True);
        var fourth = _events.Subscribe(_key, _ => calls++).AddTo(bag);
        Assert.That(fourth.IsDisposed, Is.True);
        Assert.That(_events.IsSubscribed(fourth), Is.False);
    }

    /// <summary>
    /// 令牌已取消时立即解绑且自然结束后不会重复释放
    /// </summary>
    [Test]
    public void AlreadyCanceledTokenDisposesExactlyOnce()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var handle = _events.Subscribe(_key, _ => Assert.Fail()).AddTo(canceled.Token);
        Assert.That(handle.IsDisposed, Is.True);
        var disposable = new Counter().AddTo(canceled.Token);
        Assert.That(disposable.Count, Is.EqualTo(1));
        handle.Dispose();
        _events.Publish(_key, 1);
    }

    /// <summary>
    /// 释放集合中某个对象抛错不会阻止其他清理
    /// </summary>
    [Test]
    public void BagDisposesRemainingItemsAfterFailure()
    {
        var bag = new DisposableBag();
        bag.Add(new Counter(true));
        var counter = new Counter().AddTo(bag);
        Assert.Throws<AggregateException>(() => bag.Dispose());
        Assert.That(counter.Count, Is.EqualTo(1));
        Assert.That(bag.Count, Is.Zero);
        bag.Dispose();
    }

    /// <summary>
    /// 定时器自然完成会移出集合并只通知一次
    /// </summary>
    [UnityTest]
    public IEnumerator TimerCompletionRemovesBagEntry()
    {
        using var bag = new DisposableBag();
        var calls = 0;
        var completed = 0;
        var timer = _timers.Schedule(TimeSpan.FromMilliseconds(15),
            () => calls++, onCompleted: _ => completed++).AddTo(bag);
        yield return new WaitForSecondsRealtime(0.08f);
        Assert.That(timer.IsDisposed, Is.True);
        Assert.That(_timers.IsActive(timer), Is.False);
        Assert.That(bag.Count, Is.Zero);
        Assert.That(calls, Is.EqualTo(1));
        timer.Dispose();
        Assert.That(completed, Is.EqualTo(1));
    }

    /// <summary>
    /// 禁用对象不解绑 销毁后事件和定时器一起取消
    /// </summary>
    [UnityTest]
    public IEnumerator DestroyOwnerCancelsEventAndTimerButDisableDoesNot()
    {
        var owner = Own(new GameObject("生命周期所有者"));
        var calls = 0;
        var completed = 0;
        var subscription = _events.Subscribe(_key, _ => calls++).AddTo(owner);
        var timer = _timers.Schedule(TimeSpan.FromSeconds(30),
            () => Assert.Fail(), onCompleted: _ => completed++).AddTo(owner);
        owner.SetActive(false);
        _events.Publish(_key, 1);
        Assert.That(calls, Is.EqualTo(1));
        Object.Destroy(owner);
        yield return null;
        yield return null;
        Assert.That(subscription.IsDisposed, Is.True);
        Assert.That(timer.IsDisposed, Is.True);
        Assert.That(completed, Is.EqualTo(1));
        _events.Publish(_key, 2);
        Assert.That(calls, Is.EqualTo(1));
    }

    /// <summary>
    /// 从未激活的对象销毁也会清理持有的订阅
    /// </summary>
    [UnityTest]
    public IEnumerator NeverActivatedOwnerStillReleases()
    {
        var owner = Own(new GameObject("从未激活的对象"));
        owner.SetActive(false);
        var subscription = _events.Subscribe(_key, _ => Assert.Fail()).AddTo(owner);
        Object.Destroy(owner);
        yield return null;
        yield return null;
        Assert.That(subscription.IsDisposed, Is.True);
    }

    /// <summary>
    /// 后台线程取消令牌会回到Unity主线程解绑
    /// </summary>
    [UnityTest]
    public IEnumerator BackgroundCancellationReleasesOnMainThread()
    {
        using var cancellation = new CancellationTokenSource();
        var counter = new Counter().AddTo(cancellation.Token);
        var task = Task.Run(() => cancellation.Cancel());
        yield return Await(task);
        yield return null;
        Assert.That(counter.Count, Is.EqualTo(1));
        Assert.That(counter.ThreadId, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
    }

    /// <summary>
    /// 同一预制体的两个实例各持有一次引用且注入早于启用
    /// </summary>
    [UnityTest]
    public IEnumerator ResourceInstancesReleaseTheirOwnReferences()
    {
        var firstTask = _loader.LoadAndInstantiatePrefabWithResolverAsync(Location, _container).AsTask();
        yield return Await(firstTask);
        var first = Own(firstTask.Result);
        var secondTask = _loader.LoadAndInstantiatePrefabAsync(Location).AsTask();
        yield return Await(secondTask);
        var second = Own(secondTask.Result);
        Assert.That(first.GetComponent<LifetimeOwner>(), Is.Not.Null);
        Assert.That(first.GetComponent<LifetimePanelProbe>().EnabledAfterInjection, Is.True);
        Assert.That(_loader.Loads, Is.EqualTo(2));
        Object.Destroy(first);
        yield return null;
        yield return null;
        Assert.That(_loader.Releases, Is.EqualTo(1));
        Assert.That(second != null, Is.True);
        Object.Destroy(second);
        yield return null;
        yield return null;
        Assert.That(_loader.Releases, Is.EqualTo(2));
    }

    /// <summary>
    /// 实例从未激活时销毁仍然归还预制体引用
    /// </summary>
    [UnityTest]
    public IEnumerator InactiveResourceInstanceReleases()
    {
        _prefab.SetActive(false);
        var task = _loader.LoadAndInstantiatePrefabAsync(Location).AsTask();
        yield return Await(task);
        var instance = Own(task.Result);
        Object.Destroy(instance);
        yield return null;
        yield return null;
        Assert.That(_loader.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 等待加载时取消会在加载返回后归还引用且不创建实例
    /// </summary>
    [UnityTest]
    public IEnumerator CancellationDuringLoadReturnsReference()
    {
        using var cancellation = new CancellationTokenSource();
        _loader.Delay = true;
        var task = _loader.LoadAndInstantiatePrefabAsync(Location, cancellationToken: cancellation.Token).AsTask();
        cancellation.Cancel();
        _loader.Complete();
        while (!task.IsCompleted) yield return null;
        Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        Assert.That(_loader.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 注入缺少依赖时销毁半成品并只归还一次引用
    /// </summary>
    [UnityTest]
    public IEnumerator FailedInjectionDoesNotLeakClone()
    {
        using var empty = new ContainerBuilder().Build();
        var task = _loader.LoadAndInstantiatePrefabWithResolverAsync(Location, empty).AsTask();
        while (!task.IsCompleted) yield return null;
        Assert.Throws<VContainerException>(() => task.GetAwaiter().GetResult());
        yield return null;
        yield return null;
        Assert.That(_loader.Releases, Is.EqualTo(1));
        foreach (var view in Object.FindObjectsOfType<LifetimePanelProbe>(true))
            Assert.That(view.gameObject, Is.EqualTo(_prefab));
    }

    /// <summary>
    /// 子容器请求UI时视图仍获得全局依赖 关闭及外部销毁均只释放一次
    /// </summary>
    [UnityTest]
    public IEnumerator UiUsesRootDependenciesAndAutomaticRelease()
    {
        var ui = PrepareUI();
        using var child = _container.CreateScope(builder => builder.RegisterInstance(new Dependency()));
        Assert.That(child.Resolve<IUIService>(), Is.SameAs(ui));
        var task = child.Resolve<IUIService>().OpenAsync(new UIKey<int>(1, "测试面板"), 5).AsTask();
        yield return Await(task);
        var view = Object.FindObjectOfType<LifetimePanelProbe>();
        Assert.That(view.Dependency, Is.SameAs(_container.Resolve<Dependency>()));
        Assert.That(view.EnabledAfterInjection, Is.True);
        Object.Destroy(view.gameObject);
        yield return null;
        yield return null;
        ui.DisposeImmediately(task.Result);
        Assert.That(_loader.Releases, Is.EqualTo(1));
        var second = ui.OpenAsync(new UIKey<int>(1, "测试面板"), 6).AsTask();
        yield return Await(second);
        ui.DisposeImmediately(second.Result);
        ui.DisposeImmediately(second.Result);
        yield return null;
        yield return null;
        Assert.That(_loader.Releases, Is.EqualTo(2));
    }

    /// <summary>
    /// 打开期间对象被销毁会取消面板任务并移除实例记录
    /// </summary>
    [UnityTest]
    public IEnumerator UiDestroyedDuringOpeningCancelsAndReleases()
    {
        var ui = PrepareUI();
        _prefab.GetComponent<LifetimePanelProbe>().WaitForCancellation = true;
        var task = ui.OpenAsync(new UIKey<int>(1, "测试面板"), 5).AsTask();
        var view = Object.FindObjectOfType<LifetimePanelProbe>();
        Object.Destroy(view.gameObject);
        for (var i = 0; i < 5 && !task.IsCompleted; i++) yield return null;
        Assert.That(task.IsCompleted, Is.True);
        Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        Assert.That(_loader.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 构建内存UI根节点 避免依赖产品尚未配置的UIRoot地址
    /// </summary>
    private IUIService PrepareUI()
    {
        var ui = _container.Resolve<IUIService>();
        var root = Own(new GameObject("测试UI根节点"));
        var rootType = typeof(IUIService).Assembly.GetType("UIRoot");
        var rootComponent = root.AddComponent(rootType);
        foreach (var field in rootType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (field.FieldType == typeof(Transform)) field.SetValue(rootComponent, root.transform);
        ui.GetType().GetField("_uiRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, rootComponent);
        ((IStartupService)ui).StartAsync(new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
        return ui;
    }

    /// <summary>
    /// 等待任务并传播实际异常
    /// </summary>
    private static IEnumerator Await(Task task)
    {
        while (!task.IsCompleted) yield return null;
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 用于区分全局和子容器的依赖
    /// </summary>
    public class Dependency { }

    /// <summary>
    /// 跟踪普通可释放对象及释放线程
    /// </summary>
    private class Counter : IDisposable
    {
        /// <summary>
        /// 是否在释放时抛出模拟异常
        /// </summary>
        private readonly bool _throws;
        /// <summary>
        /// 释放调用次数
        /// </summary>
        public int Count { get; private set; }
        /// <summary>
        /// 最后一次清理线程
        /// </summary>
        public int ThreadId { get; private set; }
        /// <summary>
        /// 设置模拟清理行为
        /// </summary>
        public Counter(bool throws = false) => _throws = throws;
        /// <summary>
        /// 记录清理并按需抛出异常
        /// </summary>
        public void Dispose()
        {
            Count++;
            ThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_throws) throw new InvalidOperationException("测试清理异常");
        }
    }

    /// <summary>
    /// 固定面板配置以验证真实UI服务
    /// </summary>
    private class Config : IFrameworkConfigTableService
    {
        /// <summary>
        /// 测试只查询转换后的面板配置
        /// </summary>
        public FrameworkTables FrameworkTables => throw new NotSupportedException();
        /// <summary>
        /// 返回内存预制体的挂载配置
        /// </summary>
        public bool TryGetPanelConfig(int id, out UIPanelConfig config)
        {
            config = new UIPanelConfig(id, Location, UIPanelLayer.Page);
            return true;
        }
    }

    /// <summary>
    /// 保留真实加载配对语义并统计独立引用
    /// </summary>
    private class Loader : IResourcesService
    {
        /// <summary>
        /// 内存资源模板
        /// </summary>
        private readonly GameObject _prefab;
        /// <summary>
        /// 可控加载完成信号
        /// </summary>
        private UniTaskCompletionSource<GameObject> _pending;
        /// <summary>
        /// 是否挂起加载
        /// </summary>
        public bool Delay { get; set; }
        /// <summary>
        /// 取得引用次数
        /// </summary>
        public int Loads { get; private set; }
        /// <summary>
        /// 归还引用次数
        /// </summary>
        public int Releases { get; private set; }
        /// <summary>
        /// 使用指定模板
        /// </summary>
        public Loader(GameObject prefab) => _prefab = prefab;
        /// <summary>
        /// 完成当前挂起加载
        /// </summary>
        public void Complete() => _pending?.TrySetResult(_prefab);
        /// <summary>
        /// 模拟加载成功并取得一次引用
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(string location) where T : Object
        {
            Loads++;
            var result = Delay ? await (_pending = new UniTaskCompletionSource<GameObject>()).Task : _prefab;
            return result as T;
        }
        /// <summary>
        /// 记录一次归还并检查资源类型
        /// </summary>
        public void ReleaseAsset(string location, Type assetType)
        {
            Assert.That(location, Is.EqualTo(Location));
            Assert.That(assetType, Is.EqualTo(typeof(GameObject)));
            Releases++;
            Assert.That(Releases, Is.LessThanOrEqualTo(Loads));
        }
        /// <summary>
        /// 测试不加载场景
        /// </summary>
        public SceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single,
            LocalPhysicsMode physicsMode = LocalPhysicsMode.None, bool allowSceneActivation = true, uint priority = 0) =>
            throw new NotSupportedException();
        /// <summary>
        /// 测试不卸载场景
        /// </summary>
        public UniTask UnloadSceneAsync(SceneHandle handle, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
