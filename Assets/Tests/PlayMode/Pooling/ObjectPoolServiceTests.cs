using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// 使用真实Unity对象池和VContainer验证复用及生命周期 通过可计数加载器验证资源所有权
/// </summary>
public sealed class ObjectPoolServiceTests
{
    /// <summary>测试预制体资源地址</summary>
    private const string Location = "Test/PoolProbe.prefab";
    /// <summary>当前测试的根容器</summary>
    private IObjectResolver _container;
    /// <summary>待测的实际对象池服务</summary>
    private IObjectPoolService _pool;
    /// <summary>用于记录加载和归还次数的资源加载器</summary>
    private CountingLoader _loader;
    /// <summary>用于创建测试预制体的停用父节点</summary>
    private GameObject _fixture;
    /// <summary>在内存创建的预制体模板</summary>
    private GameObject _prefab;

    /// <summary>
    /// 配置Framework真实注册并覆盖资源加载入口
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _fixture = new GameObject("PoolFixture");
        _fixture.SetActive(false);
        _prefab = new GameObject("PoolProbe");
        _prefab.transform.SetParent(_fixture.transform);
        _prefab.AddComponent<PoolProbe>();
        _loader = new CountingLoader(_prefab);
        var builder = new ContainerBuilder();
        GameComposition.RegisterDefaults(builder);
        builder.RegisterInstance<IResourcesAssetLoader>(_loader);
        builder.RegisterInstance(new PoolProbeDependency("Root"));
        _container = builder.Build();
        _pool = _container.Resolve<IObjectPoolService>();
        ((IStartupService)_pool).StartAsync(new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 等待延迟销毁及资源释放后再销毁测试模板
    /// </summary>
    /// <returns>测试清理流程</returns>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _loader.Complete();
        yield return Await(_pool.ClearAllAsync().AsTask());
        _container.Dispose();
        Object.Destroy(_fixture);
        yield return null;
    }

    /// <summary>
    /// 验证预热创建不同对象 注入早于启用且复用不会重复注入
    /// </summary>
    /// <returns>逐帧测试流程</returns>
    [UnityTest]
    public IEnumerator PrewarmReuseAndInjectionOrder()
    {
        yield return Await(_pool.PrewarmAsync(Location, 3).AsTask());
        Assert.That(_loader.Loads, Is.EqualTo(1));
        var instances = new List<GameObject>();
        for (var i = 0; i < 3; i++)
        {
            var spawning = _pool.SpawnAsync(Location).AsTask();
            yield return Await(spawning);
            instances.Add(spawning.Result);
        }
        Assert.That(new HashSet<GameObject>(instances).Count, Is.EqualTo(3));
        var first = instances[0];
        var probe = first.GetComponent<PoolProbe>();
        Assert.That(probe.EnabledAfterInjection, Is.True);
        Assert.That(probe.InjectionCount, Is.EqualTo(1));
        Assert.That(probe.SpawnCount, Is.EqualTo(1));
        first.transform.localScale = Vector3.one * 5;
        Assert.That(_pool.Despawn(first), Is.True);
        Assert.That(first.activeSelf, Is.False);
        Assert.That(_pool.Despawn(first), Is.False);
        var reused = _pool.SpawnAsync(Location).AsTask();
        yield return Await(reused);
        Assert.That(reused.Result, Is.SameAs(first));
        Assert.That(probe.SpawnCount, Is.EqualTo(2));
        Assert.That(probe.DespawnCount, Is.EqualTo(1));
        Assert.That(probe.InjectionCount, Is.EqualTo(1));
        Assert.That(first.transform.localScale, Is.EqualTo(_prefab.transform.localScale));
        Assert.That(_loader.Releases, Is.Zero);
        yield return Await(_pool.ClearAllAsync().AsTask());
        foreach (var instance in instances) Assert.That(instance == null, Is.True);
        Assert.That(_loader.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 验证取消某个等待者不会破坏共享加载 也不会重复获取资源引用
    /// </summary>
    /// <returns>等待共享加载的测试流程</returns>
    [UnityTest]
    public IEnumerator ConcurrentLoadingAndCallerCancellation()
    {
        _loader.Delay = true;
        using var cancellation = new CancellationTokenSource();
        var canceled = _pool.SpawnAsync(Location, cancellationToken: cancellation.Token).AsTask();
        var successful = _pool.SpawnAsync(Location).AsTask();
        Assert.That(_loader.Loads, Is.EqualTo(1));
        cancellation.Cancel();
        yield return null;
        Assert.Throws<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
        _loader.Complete();
        yield return Await(successful);
        Assert.That(successful.Result, Is.Not.Null);
        Assert.That(_loader.Loads, Is.EqualTo(1));
        yield return Await(_pool.ClearAllAsync().AsTask());
        Assert.That(_loader.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 验证加载期间清池不会在加载完成后留下宿主或实例
    /// </summary>
    /// <returns>等待加载结束与清理完成</returns>
    [UnityTest]
    public IEnumerator ClearDuringLoadingReturnsResource()
    {
        _loader.Delay = true;
        var spawning = _pool.SpawnAsync(Location).AsTask();
        var clearing = _pool.ClearAsync(Location).AsTask();
        var secondClearing = _pool.ClearAsync(Location).AsTask();
        Assert.That(secondClearing.IsCompleted, Is.False);
        _loader.Complete();
        yield return Await(clearing);
        yield return Await(secondClearing);
        Assert.Throws<OperationCanceledException>(() => spawning.GetAwaiter().GetResult());
        Assert.That(_loader.Releases, Is.EqualTo(1));
        var next = _pool.SpawnAsync(Location).AsTask();
        yield return Await(next);
        Assert.That(_loader.Loads, Is.EqualTo(2));
    }

    /// <summary>
    /// 验证释放期间尚未完成的加载会归还引用且拒绝新请求
    /// </summary>
    /// <returns>等待服务清理完成</returns>
    [UnityTest]
    public IEnumerator DisposeDuringLoadingDoesNotLeak()
    {
        _loader.Delay = true;
        var spawning = _pool.SpawnAsync(Location).AsTask();
        ((IStartupService)_pool).Dispose();
        _loader.Complete();
        yield return Await(_pool.ClearAllAsync().AsTask());
        Assert.Throws<OperationCanceledException>(() => spawning.GetAwaiter().GetResult());
        Assert.That(_loader.Releases, Is.EqualTo(1));
        var rejected = _pool.SpawnAsync(Location).AsTask();
        Assert.Throws<ObjectDisposedException>(() => rejected.GetAwaiter().GetResult());
    }

    /// <summary>
    /// 验证超出64个空闲实例时销毁溢出对象 同时借出数量可以超过上限
    /// </summary>
    /// <returns>等待缓存溢出销毁</returns>
    [UnityTest]
    public IEnumerator InactiveCapacityDoesNotLimitActiveCount()
    {
        var instances = new List<GameObject>();
        for (var i = 0; i < 65; i++)
        {
            var spawning = _pool.SpawnAsync(Location).AsTask();
            yield return Await(spawning);
            instances.Add(spawning.Result);
        }
        foreach (var instance in instances) Assert.That(_pool.Despawn(instance), Is.True);
        yield return null;
        Assert.That(instances[64] == null, Is.True);
        Assert.That(instances[0] != null, Is.True);
        Assert.That(_loader.Releases, Is.Zero);
        yield return Await(_pool.ClearAsync(Location).AsTask());
        Assert.That(_loader.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 验证被外部销毁的空闲对象不会被再次借出
    /// </summary>
    /// <returns>等待Unity销毁完成</returns>
    [UnityTest]
    public IEnumerator DestroyedInactiveInstanceIsSkipped()
    {
        var spawning = _pool.SpawnAsync(Location).AsTask();
        yield return Await(spawning);
        _pool.Despawn(spawning.Result);
        Object.Destroy(spawning.Result);
        yield return null;
        var replacement = _pool.SpawnAsync(Location).AsTask();
        yield return Await(replacement);
        Assert.That(replacement.Result != null, Is.True);
        Assert.That(replacement.Result, Is.Not.SameAs(spawning.Result));
    }

    /// <summary>
    /// 验证预先取消的请求不加载资源 非本池对象不会被回收
    /// </summary>
    /// <returns>测试流程</returns>
    [UnityTest]
    public IEnumerator InvalidAndCanceledRequestsDoNotAcquireResources()
    {
        var canceled = _pool.SpawnAsync(Location, cancellationToken: new CancellationToken(true)).AsTask();
        Assert.Throws<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
        var invalid = _pool.SpawnAsync("invalid").AsTask();
        Assert.Throws<ArgumentException>(() => invalid.GetAwaiter().GetResult());
        Assert.That(_pool.Despawn(_prefab), Is.False);
        Assert.That(_loader.Loads, Is.Zero);
        yield return null;
    }

    /// <summary>
    /// 等待任务并抛出原始异常
    /// </summary>
    /// <param name="task">待验证任务</param>
    /// <returns>带超时的逐帧等待</returns>
    private static IEnumerator Await(Task task)
    {
        var deadline = Time.realtimeSinceStartup + 10;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(task.IsCompleted, Is.True, "对象池测试等待超时");
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 可控制完成时机且记录引用计数的测试加载器
    /// </summary>
    private sealed class CountingLoader : IResourcesAssetLoader
    {
        /// <summary>模拟预制体资源</summary>
        private readonly GameObject _prefab;
        /// <summary>所有延迟中的加载请求</summary>
        private readonly List<UniTaskCompletionSource<GameObject>> _pending = new();
        /// <summary>是否延迟下一次加载</summary>
        public bool Delay;
        /// <summary>已获取资源引用数</summary>
        public int Loads;
        /// <summary>已归还资源引用数</summary>
        public int Releases;

        /// <summary>
        /// 绑定测试预制体
        /// </summary>
        /// <param name="prefab">测试模板</param>
        public CountingLoader(GameObject prefab) => _prefab = prefab;

        /// <summary>
        /// 记录一次资源获取并按测试控制等待
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="location">资源地址</param>
        /// <returns>模拟加载结果</returns>
        public async UniTask<T> LoadAssetAsync<T>(string location) where T : Object
        {
            Loads++;
            if (!Delay) return (T)(Object)_prefab;
            var completion = new UniTaskCompletionSource<GameObject>();
            _pending.Add(completion);
            return (T)(Object)await completion.Task;
        }

        /// <summary>
        /// 记录引用归还
        /// </summary>
        /// <param name="location">资源地址</param>
        /// <param name="assetType">资源类型</param>
        public void ReleaseAsset(string location, Type assetType) => Releases++;

        /// <summary>
        /// 完成此前所有加载 并恢复即时加载
        /// </summary>
        public void Complete()
        {
            Delay = false;
            var pending = _pending.ToArray();
            _pending.Clear();
            foreach (var completion in pending) completion.TrySetResult(_prefab);
        }
    }

}
