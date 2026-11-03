using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildingGame.Config.Framework;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 验证配置实例发布 取消处理 数据完整性与资源配对释放
/// </summary>
public class ConfigTableServiceTests
{
    /// <summary>
    /// 本用例持有并在结束时释放的内存资源加载器
    /// </summary>
    private FakeResources _resources;
    /// <summary>
    /// 用于验证配置加载与清理的真实子服务
    /// </summary>
    private IConfigTableSubService _loader;
    /// <summary>
    /// 本用例验证的框架配置访问契约
    /// </summary>
    private IFrameworkConfigTableService _config;

    /// <summary>
    /// 创建包含四种UI层级的二进制配置与内存资源加载器
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _resources = new FakeResources();
        var bytes = new ByteBuf();
        bytes.WriteSize(4);
        for (var index = 0; index < 4; index++)
        {
            bytes.WriteInt(index + 1);
            bytes.WriteString("Test/Panel");
            bytes.WriteInt(index);
        }
        _resources.Add("uipanel.bytes", new TextAsset(Encoding.UTF8.GetString(bytes.CopyData())));
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(_resources.Get("uipanel.bytes").bytes)).Replace("-", "").ToLowerInvariant();
        _resources.Add("manifest.json", new TextAsset("{\"FormatVersion\":1,\"SchemaHash\":\"" + FrameworkTables.SchemaHash +
            "\",\"Files\":[{\"Name\":\"uipanel\",\"Sha256\":\"" + hash + "\"}]}"));
        var type = typeof(IFrameworkConfigTableService).Assembly.GetType("FrameworkConfigSubService", true);
        var service = Activator.CreateInstance(type, _resources, new SilentLogger());
        _loader = (IConfigTableSubService)service;
        _config = (IFrameworkConfigTableService)service;
    }

    /// <summary>
    /// 清理测试创建的Unity对象
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        _loader.Clear();
        _resources.Dispose();
    }

    /// <summary>
    /// 未加载时必须给出明确异常
    /// </summary>
    [Test]
    public void AccessBeforeLoadFails() => Assert.Throws<InvalidOperationException>(() => _ = _config.FrameworkTables);

    /// <summary>
    /// 完整加载后正确查询与映射全部层级 并配对释放资源
    /// </summary>
    [Test]
    public async Task LoadPublishesCompleteTablesAndMapsLayers()
    {
        await _loader.LoadAsync(CancellationToken.None);
        Assert.That(_config.FrameworkTables.TbUIPanel.DataList.Count, Is.EqualTo(4));
        var expected = new[] { UIPanelLayer.Page, UIPanelLayer.Popup, UIPanelLayer.Overlay, UIPanelLayer.Toast };
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.That(_config.TryGetPanelConfig(index + 1, out var config), Is.True);
            Assert.That(config.Layer, Is.EqualTo(expected[index]));
            Assert.That(config.PrefabLocation, Is.EqualTo("Test/Panel"));
        }
        Assert.That(_config.TryGetPanelConfig(999, out _), Is.False);
        Assert.That(_resources.Releases, Is.EqualTo(2));
        _loader.Clear();
        Assert.Throws<InvalidOperationException>(() => _ = _config.FrameworkTables);
    }

    /// <summary>
    /// 数据损坏必须失败 不发布半初始化实例
    /// </summary>
    [Test]
    public void CorruptDataFailsAndReleasesResources()
    {
        _resources.Add("uipanel.bytes", new TextAsset("corrupt"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _loader.LoadAsync(CancellationToken.None));
        Assert.That(_resources.Releases, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => _ = _config.FrameworkTables);
    }

    /// <summary>
    /// 数据文件缺失时释放已经读取的清单
    /// </summary>
    [Test]
    public void MissingDataFailsWithoutPublishing()
    {
        _resources.MissingData = true;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _loader.LoadAsync(CancellationToken.None));
        Assert.That(_resources.Releases, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => _ = _config.FrameworkTables);
    }

    /// <summary>
    /// 结构版本不匹配时在读取二进制前失败
    /// </summary>
    [Test]
    public void SchemaMismatchFailsBeforeLoadingData()
    {
        _resources.Add("manifest.json", new TextAsset(_resources.Get("manifest.json").text.Replace(FrameworkTables.SchemaHash, "old-schema")));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _loader.LoadAsync(CancellationToken.None));
        Assert.That(_resources.Loads, Is.EqualTo(1));
        Assert.That(_resources.Releases, Is.EqualTo(1));
    }

    /// <summary>
    /// 失败的重新加载不能替换已有有效配置
    /// </summary>
    [Test]
    public async Task FailedReloadPreservesPreviousTables()
    {
        await _loader.LoadAsync(CancellationToken.None);
        var previous = _config.FrameworkTables;
        _resources.Add("uipanel.bytes", new TextAsset("corrupt"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _loader.LoadAsync(CancellationToken.None));
        Assert.That(_config.FrameworkTables, Is.SameAs(previous));
    }

    /// <summary>
    /// 加载过程中取消时 等资源返回后释放并停止发布
    /// </summary>
    [Test]
    public void CancellationAfterAssetArrivesReleasesIt()
    {
        using var cancellation = new CancellationTokenSource();
        _resources.OnDataLoaded = cancellation.Cancel;
        Assert.CatchAsync<OperationCanceledException>(async () => await _loader.LoadAsync(cancellation.Token));
        Assert.That(_resources.Releases, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => _ = _config.FrameworkTables);
    }

    /// <summary>
    /// 加载过程中清理时不能在稍后重新发布配置
    /// </summary>
    [Test]
    public void ClearDuringLoadInvalidatesResult()
    {
        _resources.OnDataLoaded = _loader.Clear;
        Assert.CatchAsync<OperationCanceledException>(async () => await _loader.LoadAsync(CancellationToken.None));
        Assert.That(_resources.Releases, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => _ = _config.FrameworkTables);
    }

    /// <summary>
    /// 已取消的调用不应触发任何资源读取
    /// </summary>
    [Test]
    public void AlreadyCancelledDoesNotLoadAssets()
    {
        Assert.CatchAsync<OperationCanceledException>(async () => await _loader.LoadAsync(new CancellationToken(true)));
        Assert.That(_resources.Loads, Is.Zero);
    }

    /// <summary>
    /// 提供可统计的资源加载替身
    /// </summary>
    private class FakeResources : IResourcesAssetLoader, IDisposable
    {
        /// <summary>
        /// 按资源地址索引的内存配置文件
        /// </summary>
        private readonly Dictionary<string, TextAsset> _assets = new();
        /// <summary>
        /// 本用例创建的TextAsset集合 用于统一销毁
        /// </summary>
        private readonly List<TextAsset> _owned = new();
        /// <summary>
        /// 已执行的加载次数
        /// </summary>
        public int Loads { get; private set; }
        /// <summary>
        /// 已配对的释放次数
        /// </summary>
        public int Releases { get; private set; }
        /// <summary>
        /// 模拟二进制资源不存在
        /// </summary>
        public bool MissingData { get; set; }
        /// <summary>
        /// 模拟加载完成与发布之间的生命周期变化
        /// </summary>
        public Action OnDataLoaded { get; set; }

        /// <summary>
        /// 注册资源并持有对象供测试结束时销毁
        /// </summary>
        /// <param name="name">文件名</param>
        /// <param name="asset">资源对象</param>
        public void Add(string name, TextAsset asset) { _assets[name] = asset; _owned.Add(asset); }

        /// <summary>
        /// 读取已注册的资源
        /// </summary>
        /// <param name="name">文件名</param>
        /// <returns>资源对象</returns>
        public TextAsset Get(string name) => _assets[name];

        /// <summary>
        /// 模拟即时完成的资源加载
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="location">完整资源地址</param>
        /// <returns>资源加载任务</returns>
        public UniTask<T> LoadAssetAsync<T>(string location) where T : Object
        {
            Loads++;
            var name = location.Substring(location.LastIndexOf('/') + 1);
            if (name == "uipanel.bytes")
            {
                OnDataLoaded?.Invoke();
                if (MissingData) return UniTask.FromResult<T>(null);
            }
            return UniTask.FromResult(_assets[name] as T);
        }

        /// <summary>
        /// 记录配对释放 不提前销毁测试资源
        /// </summary>
        /// <param name="location">完整资源地址</param>
        /// <param name="assetType">资源类型</param>
        public void ReleaseAsset(string location, Type assetType) => Releases++;

        /// <summary>
        /// 销毁测试持有的全部资源
        /// </summary>
        public void Dispose() { foreach (var asset in _owned) Object.DestroyImmediate(asset); }
    }

    /// <summary>
    /// 避免故意触发的异常写入Unity错误控制台
    /// </summary>
    private class SilentLogger : ILogger<FrameworkTables>
    {
        /// <summary>
        /// 接受测试日志
        /// </summary>
        /// <param name="type">日志类型</param>
        /// <param name="message">日志说明</param>
        /// <param name="exception">原始异常</param>
        /// <param name="callerFile">调用文件</param>
        /// <param name="callerLine">调用行号</param>
        public void Write(LogType type, string message, Exception exception, string callerFile, int callerLine) { }
    }
}
