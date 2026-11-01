using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>统一管理活动存档槽的读取与保存</summary>
public interface ISaveService
{
    /// <summary>当前活动的存档槽</summary>
    SaveSlotId ActiveSlot { get; }

    /// <summary>
    /// 在主线程同步采集并通过Easy Save写入活动存档槽 返回已完成的任务
    /// 未启动 已释放或保存失败时返回false 取消请求会抛出取消异常
    /// </summary>
    /// <param name="cancellationToken">写入前检查的取消令牌</param>
    /// <returns>是否保存成功</returns>
    UniTask<bool> SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>通过Easy Save保存数据段 并兼容旧JSON及有效备份恢复</summary>
internal sealed class SaveService : IStartupService, ISaveService
{
    /// <summary>框架元数据键的保留前缀</summary>
    private const string MetadataPrefix = "__framework/";
    /// <summary>容器格式版本键</summary>
    private const string FormatKey = MetadataPrefix + "formatVersion";
    /// <summary>存档槽编号键</summary>
    private const string SlotKey = MetadataPrefix + "slotId";
    /// <summary>数据段版本清单键</summary>
    private const string VersionsKey = MetadataPrefix + "sectionVersions";
    /// <summary>最近保存时间键</summary>
    private const string SavedAtKey = MetadataPrefix + "savedAtUtc";
    /// <summary>Easy Save原生备份文件使用的扩展后缀</summary>
    private const string BackupSuffix = ".bac";
    /// <summary>按注册顺序采集和应用数据段</summary>
    private readonly IReadOnlyList<ISaveDataHandler> _handlers;
    /// <summary>用于校验版本及查找旧数据迁移处理器</summary>
    private readonly Dictionary<string, ISaveDataHandler> _handlersByKey;
    /// <summary>存档根目录</summary>
    private readonly string _saveDirectory;
    /// <summary>存档诊断日志</summary>
    private readonly ILogger<SaveService> _logger;
    /// <summary>框架生命周期令牌</summary>
    private CancellationToken _lifetimeToken;
    /// <summary>服务是否已完成初始化</summary>
    private bool _started;
    /// <summary>服务是否已释放</summary>
    private bool _disposed;
    /// <summary>发生无法恢复或不兼容的读档错误后禁止覆盖原文件</summary>
    private bool _canSave;

    /// <summary>启动优先级</summary>
    public int Priority => FrameworkSettings.SaveStartupPriority;
    /// <summary>启动进度描述</summary>
    public string Description => "存档服务";
    /// <summary>启动进度权重</summary>
    public float Weight => FrameworkSettings.SaveStartupWeight;
    /// <summary>当前活动的存档槽</summary>
    public SaveSlotId ActiveSlot { get; private set; }

    /// <summary>绑定处理器与日志 并使用Unity持久化目录</summary>
    /// <param name="handlers">已注册的数据段处理器</param>
    /// <param name="logger">日志记录器</param>
    [VContainer.Inject]
    public SaveService(IEnumerable<ISaveDataHandler> handlers, ILogger<SaveService> logger)
        : this(handlers, logger, Path.Combine(Application.persistentDataPath, "SaveSlots")) { }

    /// <summary>使用指定目录创建服务 便于隔离文件系统测试</summary>
    /// <param name="handlers">已注册的数据段处理器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="saveDirectory">独立存档目录</param>
    internal SaveService(IEnumerable<ISaveDataHandler> handlers, ILogger<SaveService> logger, string saveDirectory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handlers = (handlers ?? throw new ArgumentNullException(nameof(handlers))).ToList();
        _saveDirectory = Path.GetFullPath(saveDirectory);
        _handlersByKey = new Dictionary<string, ISaveDataHandler>(StringComparer.Ordinal);
        foreach (var handler in _handlers)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.Key) ||
                handler.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) || handler.CurrentVersion < 1)
                throw new InvalidOperationException("存档处理器的标识或版本无效");
            if (!_handlersByKey.TryAdd(handler.Key, handler))
                throw new InvalidOperationException($"存档数据段标识重复:{handler.Key}");
        }
    }

    /// <summary>读取默认槽 优先使用ES3文件 缺失时迁移旧JSON</summary>
    /// <param name="context">框架启动上下文</param>
    /// <returns>已完成的启动任务</returns>
    public UniTask StartAsync(StartupContext context)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SaveService));
        if (_started) return UniTask.CompletedTask;
        _lifetimeToken = context.LifetimeToken;
        _lifetimeToken.ThrowIfCancellationRequested();
        ActiveSlot = SaveSlotId.Default;
        ResetHandlers();
        var settings = CreateSettings(GetPath(".es3"));
        try
        {
            if (ES3.FileExists(settings) || ES3.FileExists(CreateSettings(settings.path + BackupSuffix)))
                LoadWithBackup(settings);
            else
            {
                _canSave = true;
                if (File.Exists(GetPath(".json")))
                {
                    LoadLegacy();
                    _lifetimeToken.ThrowIfCancellationRequested();
                    if (SaveCurrent(settings)) _logger.Info("旧JSON存档已迁移为Easy Save存档 原文件已保留");
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _canSave = false;
            ResetHandlers();
            _logger.Error("读取存档失败 已恢复默认数据并禁止覆盖原存档", exception);
        }
        _lifetimeToken.ThrowIfCancellationRequested();
        _started = true;
        return UniTask.CompletedTask;
    }

    /// <summary>先完整序列化所有数据段 再创建备份并提交到磁盘</summary>
    /// <param name="cancellationToken">写入前检查的取消令牌</param>
    /// <returns>是否保存成功</returns>
    public UniTask<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_started || _disposed || !_canSave) return UniTask.FromResult(false);
        _lifetimeToken.ThrowIfCancellationRequested();
        return UniTask.FromResult(SaveCurrent(CreateSettings(GetPath(".es3")), cancellationToken));
    }

    /// <summary>停止接受后续保存请求</summary>
    public void Dispose()
    {
        _disposed = true;
        _canSave = false;
    }

    /// <summary>完整提交快照 数据段失败时不触碰磁盘上的旧存档</summary>
    /// <param name="settings">目标文件设置</param>
    /// <param name="cancellationToken">写入前检查的取消令牌</param>
    /// <returns>是否完成提交</returns>
    private bool SaveCurrent(ES3Settings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = ES3.FileExists(settings);
            var file = exists ? ReadFile(settings) : new ES3File(settings, false);
            var versions = exists ? ReadVersions(file) : new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var handler in _handlers)
            {
                handler.Save(file);
                if (!file.KeyExists(handler.Key)) throw new InvalidDataException($"存档数据段未写入:{handler.Key}");
                versions[handler.Key] = handler.CurrentVersion;
            }
            file.Save(FormatKey, FrameworkSettings.SaveFormatVersion);
            file.Save(SlotKey, ActiveSlot.Id);
            file.Save(VersionsKey, versions);
            file.Save(SavedAtKey, DateTime.UtcNow.ToString("O"));
            cancellationToken.ThrowIfCancellationRequested();
            _lifetimeToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_saveDirectory);
            if (exists) ES3.CreateBackup(settings);
            file.Sync();
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.Error($"保存Easy Save存档失败:{settings.path}", exception);
            return false;
        }
    }

    /// <summary>损坏时读取有效备份 不兼容的新版本存档禁止回退覆盖</summary>
    /// <param name="settings">正式存档设置</param>
    private void LoadWithBackup(ES3Settings settings)
    {
        if (ES3.FileExists(settings))
        {
            try
            {
                LoadFile(settings);
                _canSave = true;
                return;
            }
            catch (NotSupportedException) { throw; }
            catch (Exception exception)
            {
                _logger.Error("正式存档读取失败 将尝试备份", exception);
                ResetHandlers();
            }
        }
        var backup = CreateSettings(settings.path + BackupSuffix);
        if (!ES3.FileExists(backup)) throw new InvalidDataException("没有可用的存档备份");
        LoadFile(backup);
        ES3.CopyFile(backup, settings);
        _canSave = true;
        _logger.Info("已从Easy Save备份恢复存档");
    }

    /// <summary>校验容器与数据段版本后应用所有已注册数据</summary>
    /// <param name="settings">待读取的文件设置</param>
    private void LoadFile(ES3Settings settings)
    {
        var file = ReadFile(settings);
        var versions = ReadVersions(file);
        foreach (var handler in _handlers)
            if (versions.TryGetValue(handler.Key, out var version)) handler.Load(file, version);
    }

    /// <summary>读取版本清单并拒绝缺失数据及不支持的版本</summary>
    /// <param name="file">Easy Save文件缓存</param>
    /// <returns>数据段标识到版本的映射</returns>
    private Dictionary<string, int> ReadVersions(ES3File file)
    {
        if (file.Load<int>(FormatKey) != FrameworkSettings.SaveFormatVersion)
            throw new NotSupportedException("Easy Save存档格式版本不受支持");
        if (file.Load<int>(SlotKey) != ActiveSlot.Id) throw new InvalidDataException("存档槽编号不匹配");
        var versions = file.Load<Dictionary<string, int>>(VersionsKey);
        if (versions == null) throw new InvalidDataException("存档缺少数据段版本清单");
        foreach (var pair in versions)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) ||
                pair.Value < 1 || !file.KeyExists(pair.Key))
                throw new InvalidDataException("存档数据段清单无效");
            if (_handlersByKey.TryGetValue(pair.Key, out var handler) && pair.Value > handler.CurrentVersion)
                throw new NotSupportedException($"存档数据段版本高于当前程序:{pair.Key}");
        }
        return versions;
    }

    /// <summary>仅在不存在ES3存档时读取旧格式 保留原JSON文件</summary>
    private void LoadLegacy()
    {
        var envelope = JObject.Parse(File.ReadAllText(GetPath(".json")));
        if (envelope.Value<int?>("FormatVersion") != FrameworkSettings.LegacySaveFormatVersion)
            throw new NotSupportedException("旧JSON存档格式版本不受支持");
        if (envelope.Value<int?>("SlotId") != ActiveSlot.Id || envelope["Sections"] is not JArray sections)
            throw new InvalidDataException("旧JSON存档结构无效");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            var key = section.Value<string>("Key");
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) throw new InvalidDataException("旧存档数据段标识无效或重复");
            if (!_handlersByKey.TryGetValue(key, out var handler) || handler is not ILegacyJsonSaveDataHandler legacy)
                throw new NotSupportedException($"旧存档缺少迁移处理器:{key}");
            var version = section.Value<int?>("Version");
            if (!version.HasValue || version < 1 || version > handler.CurrentVersion)
                throw new NotSupportedException($"旧存档数据段版本不受支持:{key}");
            if (section["Payload"] is not JObject payload) throw new InvalidDataException($"旧存档数据段无效:{key}");
            legacy.LoadLegacy(payload);
        }
    }

    /// <summary>清除可能只加载了一部分的数据段状态</summary>
    private void ResetHandlers()
    {
        foreach (var handler in _handlers) handler.Reset();
    }

    /// <summary>读取旧Framework类型头时映射到Game 仅在本次读取期间注册解析回调</summary>
    /// <param name="settings">待读取的存档设置</param>
    /// <returns>已解析类型信息的文件缓存</returns>
    private static ES3File ReadFile(ES3Settings settings)
    {
        // ES3通过程序集限定名定位数据类型 保留旧名称解析避免合并程序集后丢失数据段
        AppDomain.CurrentDomain.AssemblyResolve += ResolveLegacyAssembly;
        try { return new ES3File(settings, true); }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= ResolveLegacyAssembly; }
    }

    /// <summary>将旧存档中的Framework程序集身份映射到当前游戏程序集</summary>
    /// <param name="sender">发起解析的应用域</param>
    /// <param name="args">原程序集的标识</param>
    /// <returns>当前游戏程序集 未匹配时不处理</returns>
    private static Assembly ResolveLegacyAssembly(object sender, ResolveEventArgs args) =>
        new AssemblyName(args.Name).Name == "Framework" ? typeof(SaveService).Assembly : null;

    /// <summary>生成当前槽位的存档路径</summary>
    /// <param name="extension">文件扩展名</param>
    /// <returns>存档绝对路径</returns>
    private string GetPath(string extension) => Path.Combine(_saveDirectory, ActiveSlot + extension);

    /// <summary>固定本地文件与按值序列化 不依赖场景引用管理器或全局默认设置</summary>
    /// <param name="path">文件绝对路径</param>
    /// <returns>服务专用的Easy Save设置</returns>
    private static ES3Settings CreateSettings(string path) => new(false)
    {
        path = path,
        location = ES3.Location.File,
        encryptionType = ES3.EncryptionType.None,
        compressionType = ES3.CompressionType.None,
        referenceMode = ES3.ReferenceMode.ByValue,
        memberReferenceMode = ES3.ReferenceMode.ByValue
    };
}
