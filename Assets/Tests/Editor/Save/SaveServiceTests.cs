using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VContainer;

/// <summary>使用真实Easy Save文件验证存档 备份及迁移 不访问玩家存档目录</summary>
public sealed class SaveServiceTests
{
    /// <summary>每个用例的独立临时目录</summary>
    private string _directory;
    /// <summary>框架容器提供真实音频设置和事件服务</summary>
    private IObjectResolver _container;
    /// <summary>测试创建的服务 结束后统一释放</summary>
    private readonly List<IStartupService> _services = new();
    /// <summary>默认槽位的实际ES3路径</summary>
    private string SavePath => Path.Combine(_directory, "slot_000.es3");

    /// <summary>创建独立目录并启动真实事件服务</summary>
    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "BuildingGame.SaveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var builder = new ContainerBuilder();
        GameComposition.RegisterDefaults(builder);
        _container = builder.Build();
        ((IStartupService)_container.Resolve<IEventService>()).StartAsync(
            new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();
    }

    /// <summary>释放服务后仅删除本用例已确认位于测试根目录内的文件</summary>
    [TearDown]
    public void TearDown()
    {
        foreach (var service in _services) service.Dispose();
        _services.Clear();
        _container?.Dispose();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BuildingGame.SaveTests")) + Path.DirectorySeparatorChar;
        if (_directory != null && Path.GetFullPath(_directory).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(_directory, true);
    }

    /// <summary>验证容器默认注册仍可解析统一存档入口</summary>
    [Test]
    public void DefaultCompositionResolvesSaveService()
    {
        Assert.That(_container.Resolve<ISaveService>(), Is.InstanceOf<IStartupService>());
        Assert.That(typeof(ISaveService).Assembly.GetName().Name, Is.EqualTo("Game"));
        Assert.That(typeof(GameLifetimeScope).Assembly, Is.SameAs(typeof(ISaveService).Assembly));
    }

    /// <summary>合并程序集后仍能加载旧Framework类型头 正式文件与备份均可恢复</summary>
    /// <param name="fromBackup">是否仅保留旧程序集的备份</param>
    [TestCase(false)]
    [TestCase(true)]
    public void LoadsSaveFromPreviousFrameworkAssembly(bool fromBackup)
    {
        var handlers = _container.Resolve<IEnumerable<ISaveDataHandler>>().ToArray();
        var audio = _container.Resolve<IAudioSettings>();
        var originalService = CreateService(handlers);
        Start(originalService);
        audio.SetMusicEnabled(false);
        audio.SetMusicVolume(0.23f);
        audio.SetSfxVolume(0.67f);
        Assert.That(Save(originalService), Is.True);
        var current = File.ReadAllText(SavePath);
        var legacy = current.Replace("AudioSettingsSaveData,Game", "AudioSettingsSaveData,Framework");
        Assert.That(legacy, Is.Not.EqualTo(current));
        File.WriteAllText(fromBackup ? SavePath + ".bac" : SavePath, legacy);
        if (fromBackup) File.Delete(SavePath);
        Assert.That(Type.GetType("AudioSettingsSaveData,Framework"), Is.Null);
        var migrated = CreateService(handlers);
        Start(migrated);
        Assert.That(audio.MusicEnabled, Is.False);
        Assert.That(audio.MusicVolume, Is.EqualTo(0.23f));
        Assert.That(audio.SfxVolume, Is.EqualTo(0.67f));
        Assert.That(Save(migrated), Is.True);
        StringAssert.Contains("AudioSettingsSaveData,Game", File.ReadAllText(SavePath));
        StringAssert.Contains("AudioSettingsSaveData,Framework", File.ReadAllText(SavePath + ".bac"));
    }

    /// <summary>真实音频数据直接经ES3序列化后由新服务重新加载并广播变化</summary>
    [Test]
    public void AudioSettingsRoundTripThroughEasySave()
    {
        var handlers = _container.Resolve<IEnumerable<ISaveDataHandler>>().ToArray();
        var audio = _container.Resolve<IAudioSettings>();
        var service = CreateService(handlers);
        Start(service);
        audio.SetMusicEnabled(false);
        audio.SetSfxEnabled(false);
        audio.SetMusicVolume(0.25f);
        audio.SetSfxVolume(0.65f);
        Assert.That(Save(service), Is.True);
        Assert.That(File.Exists(SavePath), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "slot_000.json")), Is.False);
        audio.SetMusicVolume(0.9f);
        var notifications = 0;
        _container.Resolve<IEventService>().Subscribe(FrameworkEventKeys.AudioSettingsChanged, _ => notifications++);
        Start(CreateService(handlers));
        Assert.That(audio.MusicEnabled, Is.False);
        Assert.That(audio.SfxEnabled, Is.False);
        Assert.That(audio.MusicVolume, Is.EqualTo(0.25f));
        Assert.That(audio.SfxVolume, Is.EqualTo(0.65f));
        Assert.That(notifications, Is.GreaterThan(0));
    }

    /// <summary>序列化失败不覆盖正式文件及已有备份</summary>
    [Test]
    public void SnapshotFailurePreservesPrimaryAndBackup()
    {
        var handler = new ValueHandler();
        var service = CreateService(handler);
        Start(service);
        handler.Value = 4;
        Assert.That(Save(service), Is.True);
        handler.Value = 8;
        Assert.That(Save(service), Is.True);
        var original = File.ReadAllBytes(SavePath);
        var backup = File.ReadAllBytes(SavePath + ".bac");
        handler.FailSave = true;
        Assert.That(Save(service), Is.False);
        Assert.That(File.ReadAllBytes(SavePath), Is.EqualTo(original));
        Assert.That(File.ReadAllBytes(SavePath + ".bac"), Is.EqualTo(backup));
    }

    /// <summary>正式文件损坏或缺失时恢复上一轮有效备份并允许继续保存</summary>
    /// <param name="deletePrimary">是否模拟正式文件缺失</param>
    [TestCase(false)]
    [TestCase(true)]
    public void RecoversValidBackup(bool deletePrimary)
    {
        var handler = new ValueHandler();
        var service = CreateService(handler);
        Start(service);
        handler.Value = 4;
        Assert.That(Save(service), Is.True);
        handler.Value = 8;
        Assert.That(Save(service), Is.True);
        if (deletePrimary) File.Delete(SavePath);
        else File.WriteAllText(SavePath, "损坏的存档");
        var restored = CreateService(handler);
        Start(restored);
        Assert.That(handler.Value, Is.EqualTo(4));
        Assert.That(ES3.Load<int>(handler.Key, Settings()), Is.EqualTo(4));
        Assert.That(File.Exists(SavePath + ".bac"), Is.True);
        Assert.That(Save(restored), Is.True);
    }

    /// <summary>不可恢复的数据只恢复运行时默认值 保留原文件且拒绝后续覆盖</summary>
    [Test]
    public void CorruptSaveWithoutBackupIsNotOverwritten()
    {
        File.WriteAllText(SavePath, "损坏的存档");
        var handler = new ValueHandler { Value = 99 };
        var service = CreateService(handler);
        Start(service);
        Assert.That(handler.Value, Is.Zero);
        Assert.That(Save(service), Is.False);
        Assert.That(File.ReadAllText(SavePath), Is.EqualTo("损坏的存档"));
    }

    /// <summary>程序版本落后时不回退到旧备份并覆盖更新版本的数据</summary>
    /// <param name="newerSection">是否仅提高数据段版本</param>
    [TestCase(false)]
    [TestCase(true)]
    public void NewerVersionsAreProtected(bool newerSection)
    {
        var handler = new ValueHandler();
        var service = CreateService(handler);
        Start(service);
        Assert.That(Save(service), Is.True);
        Assert.That(Save(service), Is.True);
        if (newerSection)
            ES3.Save("__framework/sectionVersions", new Dictionary<string, int> { [handler.Key] = 99 }, Settings());
        else ES3.Save("__framework/formatVersion", 99, Settings());
        var original = File.ReadAllBytes(SavePath);
        var olderProgram = CreateService(handler);
        Start(olderProgram);
        Assert.That(Save(olderProgram), Is.False);
        Assert.That(File.ReadAllBytes(SavePath), Is.EqualTo(original));
    }

    /// <summary>旧JSON音频设置可迁移且保留原文件 后续优先读取ES3</summary>
    [Test]
    public void MigratesLegacyJsonAndPrefersEasySaveAfterwards()
    {
        var legacyPath = Path.Combine(_directory, "slot_000.json");
        const string LegacyJson = "{\"FormatVersion\":2,\"SlotId\":0,\"Sections\":[{\"Key\":\"audioSettings\",\"Version\":1,\"Payload\":{\"MusicEnabled\":false,\"SfxEnabled\":true,\"MusicVolume\":0.2,\"SfxVolume\":0.7}}]}";
        File.WriteAllText(legacyPath, LegacyJson);
        var handlers = _container.Resolve<IEnumerable<ISaveDataHandler>>().ToArray();
        var audio = _container.Resolve<IAudioSettings>();
        var service = CreateService(handlers);
        Start(service);
        Assert.That(File.Exists(SavePath), Is.True);
        Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(LegacyJson));
        Assert.That(audio.MusicVolume, Is.EqualTo(0.2f));
        Assert.That(audio.MusicEnabled, Is.False);
        audio.SetMusicVolume(0.6f);
        Assert.That(Save(service), Is.True);
        Start(CreateService(handlers));
        Assert.That(audio.MusicVolume, Is.EqualTo(0.6f));
        Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(LegacyJson));
    }

    /// <summary>部分数据段应用失败后恢复所有默认值且不保存部分加载状态</summary>
    [Test]
    public void InvalidAudioDataRollsBackOtherSections()
    {
        var audioHandler = _container.Resolve<IEnumerable<ISaveDataHandler>>().Single();
        var valueHandler = new ValueHandler();
        var service = CreateService(valueHandler, audioHandler);
        Start(service);
        valueHandler.Value = 42;
        Assert.That(Save(service), Is.True);
        var text = File.ReadAllText(SavePath);
        var invalid = System.Text.RegularExpressions.Regex.Replace(text, "(\"MusicVolume\"\\s*:\\s*)1(?:\\.0+)?", "$1-2");
        Assert.That(invalid, Is.Not.EqualTo(text));
        File.WriteAllText(SavePath, invalid);
        var original = File.ReadAllBytes(SavePath);
        var restarted = CreateService(valueHandler, audioHandler);
        Start(restarted);
        Assert.That(valueHandler.Value, Is.Zero);
        Assert.That(_container.Resolve<IAudioSettings>().MusicVolume, Is.EqualTo(1f));
        Assert.That(Save(restarted), Is.False);
        Assert.That(File.ReadAllBytes(SavePath), Is.EqualTo(original));
    }

    /// <summary>暂时未注册的已保存数据段在其他数据保存时继续保留</summary>
    [Test]
    public void PreservesUnregisteredSections()
    {
        var first = new ValueHandler { Key = "first" };
        var second = new ValueHandler { Key = "second" };
        var service = CreateService(first, second);
        Start(service);
        first.Value = 3;
        second.Value = 7;
        Assert.That(Save(service), Is.True);
        var reduced = CreateService(first);
        Start(reduced);
        first.Value = 9;
        Assert.That(Save(reduced), Is.True);
        Assert.That(ES3.Load<int>(second.Key, Settings()), Is.EqualTo(7));
        Start(CreateService(first, second));
        Assert.That(second.Value, Is.EqualTo(7));
    }

    /// <summary>启动前 释放后和取消时不会写入磁盘</summary>
    [Test]
    public void LifecycleAndCancellationDoNotWriteFiles()
    {
        var service = CreateService(new ValueHandler());
        Assert.That(Save(service), Is.False);
        Start(service);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => { service.SaveAsync(cancellation.Token).GetAwaiter().GetResult(); });
        Assert.That(File.Exists(SavePath), Is.False);
        ((IStartupService)service).Dispose();
        Assert.That(Save(service), Is.False);
    }

    /// <summary>通过内部目录构造器隔离玩家文件 同时使用真实服务实现</summary>
    /// <param name="handlers">本次测试的数据处理器</param>
    /// <returns>真实存档服务</returns>
    private ISaveService CreateService(params ISaveDataHandler[] handlers)
    {
        var type = typeof(ISaveService).Assembly.GetType("SaveService", true);
        var logger = Activator.CreateInstance(typeof(TestLogger<>).MakeGenericType(type));
        var instance = (ISaveService)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { handlers, logger, _directory }, null);
        _services.Add((IStartupService)instance);
        return instance;
    }

    /// <summary>启动真实服务并同步检查已完成任务的异常</summary>
    /// <param name="service">待启动服务</param>
    private static void Start(ISaveService service) => ((IStartupService)service).StartAsync(
        new StartupContext(CancellationToken.None, null)).GetAwaiter().GetResult();

    /// <summary>执行真实文件保存</summary>
    /// <param name="service">待保存服务</param>
    /// <returns>保存结果</returns>
    private static bool Save(ISaveService service) => service.SaveAsync().GetAwaiter().GetResult();

    /// <summary>生成测试读取及损坏模拟使用的文件设置</summary>
    /// <returns>固定使用本地测试文件的设置</returns>
    private ES3Settings Settings() => new(false) { path = SavePath, location = ES3.Location.File };

    /// <summary>用简单值覆盖快照失败及恢复路径 序列化仍由真实ES3执行</summary>
    private sealed class ValueHandler : ISaveDataHandler
    {
        /// <summary>用于模拟存档变化的运行时数据</summary>
        public int Value;
        /// <summary>是否在采集阶段模拟异常</summary>
        public bool FailSave;
        /// <summary>可配置的数据段标识</summary>
        public string Key { get; set; } = "testValue";
        /// <summary>测试数据版本</summary>
        public int CurrentVersion => 1;
        /// <summary>恢复默认值</summary>
        public void Reset() => Value = 0;
        /// <summary>返回待序列化数据或模拟失败</summary>
        /// <param name="file">待提交的文件缓存</param>
        public void Save(ES3File file)
        {
            if (FailSave) throw new InvalidOperationException("模拟数据快照失败");
            file.Save(Key, Value);
        }
        /// <summary>从真实ES3缓存载入数据</summary>
        /// <param name="file">文件缓存</param>
        /// <param name="savedVersion">数据版本</param>
        public void Load(ES3File file, int savedVersion) => Value = file.Load<int>(Key);
    }

    /// <summary>接收预期错误 避免故障场景向Unity控制台输出错误日志</summary>
    /// <typeparam name="T">日志分类</typeparam>
    public sealed class TestLogger<T> : ILogger<T>
    {
        /// <summary>接收本用例预期产生的诊断信息</summary>
        /// <param name="type">日志类型</param>
        /// <param name="message">日志说明</param>
        /// <param name="exception">原始异常</param>
        /// <param name="callerFile">调用文件</param>
        /// <param name="callerLine">调用行号</param>
        public void Write(LogType type, string message, Exception exception, string callerFile, int callerLine) { }
    }
}
