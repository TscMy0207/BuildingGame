using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger;
using ZLogger.Providers;
using Debug = UnityEngine.Debug;

/// <summary>
/// 日志适配接口 为框架日志提供分类输出
/// </summary>
internal interface ILoggingService
{
    /// <summary>
    /// 输出日志并保留原始异常及调用位置
    /// </summary>
    void Write(string category, LogType type, string message, Exception exception, string callerFile, int callerLine);
}

/// <summary>
/// 管理ZLogger控制台和滚动文件输出 退出时排空文件队列并释放资源
/// </summary>
[AutoRegister(typeof(ILoggingService), typeof(IStartupService), AsSelf = false)]
internal class LoggingService : ISystem, IStartupService, ILoggingService
{
    /// <summary>
    /// 避免ZLogger控制台输出和内部诊断再次被Unity日志回调收集
    /// </summary>
    [ThreadStatic]
    private static bool t_isForwarding;

    /// <summary>
    /// 协调日志写入和释放 防止关闭后向后台队列提交新条目
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    /// 负责滚动文件输出并在释放时排空后台队列的日志工厂
    /// </summary>
    private readonly ILoggerFactory _fileFactory;

    /// <summary>
    /// 仅向文件输出的Unity原生日志分类
    /// </summary>
    private readonly Microsoft.Extensions.Logging.ILogger _unityLogger;

    /// <summary>
    /// 当前日志目录和会话标识 避免不同运行会话共用文件
    /// </summary>
    private readonly string _directory;
    /// <summary>
    /// 本次日志会话的唯一文件名前缀
    /// </summary>
    private readonly string _sessionName;
    /// <summary>
    /// 原子读取的释放标记 阻止关闭后继续提交日志
    /// </summary>
    private int _isDisposed;

    /// <summary>
    /// 创建输出目标 使日志在其他服务启动前即可使用
    /// </summary>
    [VContainer.Inject]
    public LoggingService() : this(Path.Combine(Application.persistentDataPath, "Logs")) { }

    /// <summary>
    /// 使用独立目录创建日志输出 供隔离验证使用
    /// </summary>
    private LoggingService(string directory)
    {
        _directory = directory;
        _sessionName = $"BuildingGame-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}";
        _fileFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddZLoggerRollingFile(options =>
            {
                options.FilePathSelector = SelectLogFile;
                // 此版本使用按日检查点才能在大小滚动时持续递增序号
                options.RollingInterval = RollingInterval.Day;
                options.RollingSizeKB = FrameworkSettings.LoggingMaxFileSizeMb * 1024;
                options.InternalErrorLogger = ReportInternalError;
                // 消息和诊断已统一准备 不再追加前后缀或重复格式化异常
                options.UsePlainTextFormatter();
            });
        });

        _unityLogger = _fileFactory.CreateLogger("Unity");
        Application.logMessageReceivedThreaded += HandleUnityLog;
        Application.quitting += Dispose;
    }

    #region 服务生命周期

    /// <summary>
    /// 日志服务的启动优先级
    /// </summary>
    public int Priority => FrameworkSettings.LoggingStartupPriority;

    /// <summary>
    /// 启动界面显示的服务名称
    /// </summary>
    public string Description => "日志服务";

    /// <summary>
    /// 日志服务在启动进度中的权重
    /// </summary>
    public float Weight => FrameworkSettings.LoggingStartupWeight;

    /// <summary>
    /// 配合框架启动流程 日志输出已在构造时就绪
    /// </summary>
    public UniTask StartAsync(StartupContext context) => UniTask.CompletedTask;

    /// <summary>
    /// 停止收集Unity日志 等待已提交的文件日志写完 并允许重复释放
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
            Application.logMessageReceivedThreaded -= HandleUnityLog;
            Application.quitting -= Dispose;
            _fileFactory.Dispose();
        }
    }

    #endregion

    /// <summary>
    /// 按LogType分流 普通日志仅控制台 警告和错误类同时写入文件
    /// </summary>
    public void Write(string category, LogType type, string message, Exception exception, string callerFile, int callerLine)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _isDisposed) != 0) return;

            var diagnostics = PrepareDiagnostics(type, exception, callerFile, callerLine);
            var entry = FormatEntry(category, type, message, diagnostics);
            if (type != LogType.Log)
                _fileFactory.CreateLogger(category).ZLog(ToLogLevel(type), $"{entry}", filePath: callerFile, lineNumber: callerLine);

            var wasForwarding = t_isForwarding;
            t_isForwarding = true;
            try
            {
                // 直接输出已准备的文本 不启用PrettyStacktrace或Unity自动堆栈采集
                var consoleType = type == LogType.Log ? LogType.Log : type == LogType.Warning ? LogType.Warning : LogType.Error;
                Debug.LogFormat(consoleType, LogOption.NoStacktrace, null, "{0}", entry);
            }
            finally
            {
                t_isForwarding = wasForwarding;
            }
        }
    }

    /// <summary>
    /// 一次性准备诊断文本 异常使用原始信息 普通日志不采集调用堆栈
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string PrepareDiagnostics(LogType type, Exception exception, string callerFile, int callerLine)
    {
        if (exception != null)
        {
            var diagnostics = exception.ToString();
            if (string.IsNullOrWhiteSpace(exception.StackTrace) && exception.InnerException == null)
                diagnostics += "\n异常堆栈不可用（异常未抛出或运行时未提供）";
            else if (!HasExceptionSourceLocation(exception))
                diagnostics += "\n异常源码行号不可用，以上为异常保留的运行时信息";
            return AppendCallerLocation(diagnostics, "记录位置", callerFile, callerLine);
        }

        if (type == LogType.Log)
            return AppendCallerLocation("", "调用位置", callerFile, callerLine);

        return FormatCallStack(new StackTrace(1, true), callerFile, callerLine);
    }

    /// <summary>
    /// 保留运行时实际提供的堆栈 缺少源码信息时明确说明并补充编译期调用位置
    /// </summary>
    private static string FormatCallStack(StackTrace stack, string callerFile, int callerLine)
    {
        var diagnostics = stack.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(diagnostics))
            diagnostics = "调用堆栈不可用";
        else if (!HasSourceLocation(stack, null, 0))
            diagnostics += "\n堆栈源码行号不可用，以上为运行时可用的调用信息";

        if (!HasSourceLocation(stack, callerFile, callerLine) || string.IsNullOrEmpty(callerFile) || callerLine <= 0)
            diagnostics = AppendCallerLocation(diagnostics, "调用位置", callerFile, callerLine);
        return diagnostics;
    }

    /// <summary>
    /// 检查异常自身与内部异常保留的源码位置 不将内部异常误判为没有堆栈
    /// </summary>
    private static bool HasExceptionSourceLocation(Exception exception)
    {
        if (HasSourceLocation(new StackTrace(exception, true), null, 0)) return true;
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                if (HasExceptionSourceLocation(inner)) return true;
            return false;
        }
        return exception.InnerException != null && HasExceptionSourceLocation(exception.InnerException);
    }

    /// <summary>
    /// 判断堆栈是否含有效源码行号 指定调用位置时同时核对路径与行号
    /// </summary>
    private static bool HasSourceLocation(StackTrace stack, string callerFile, int callerLine)
    {
        var frames = stack.GetFrames();
        if (frames == null) return false;
        foreach (var frame in frames)
        {
            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();
            if (string.IsNullOrEmpty(file) || line <= 0) continue;
            if (callerFile == null) return true;
            // 只统一分隔符 不折叠大小写 避免在区分大小写的平台误判为同一路径
            if (line == callerLine && string.Equals(file.Replace('\\', '/'), callerFile.Replace('\\', '/'), StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 补充编译期调用位置 未提供位置时明确标记 不伪造文件或行号
    /// </summary>
    private static string AppendCallerLocation(string diagnostics, string label, string callerFile, int callerLine)
    {
        var separator = string.IsNullOrEmpty(diagnostics) ? "" : "\n";
        if (string.IsNullOrEmpty(callerFile))
            return diagnostics + separator + label + "不可用（未提供编译期调用位置）";
        if (callerLine <= 0)
            return diagnostics + separator + label + "（编译期）: " + callerFile + "（行号不可用）";
        return diagnostics + separator + label + "（编译期）: " + callerFile + ":" + callerLine;
    }

    /// <summary>
    /// 统一时间 中文等级 模块和消息 两个输出目标复用完整文本
    /// </summary>
    private static string FormatEntry(string category, LogType type, string message, string diagnostics)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var entry = $"[{timestamp}] [{GetLevelName(ToLogLevel(type))}] [{category}] {message}";
        return string.IsNullOrEmpty(diagnostics) ? entry : entry + "\n" + diagnostics;
    }

    /// <summary>
    /// 按会话和UTC日期选择日志文件 只清理超过保留数量的旧文本文件
    /// </summary>
    private string SelectLogFile(DateTimeOffset timestamp, int sequence)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{_sessionName}-{timestamp.UtcDateTime:yyyyMMdd}-{sequence:D6}.log");
        try
        {
            var files = Directory.GetFiles(_directory, "BuildingGame-*.log");
            Array.Sort(files, StringComparer.Ordinal);
            var removeCount = files.Length - Math.Max(1, FrameworkSettings.LoggingMaxFileCount) + 1;
            for (var i = 0; i < removeCount; i++)
            {
                if (!string.Equals(files[i], path, StringComparison.OrdinalIgnoreCase))
                    File.Delete(files[i]);
            }
        }
        catch (IOException exception)
        {
            ReportInternalError(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportInternalError(exception);
        }
        return path;
    }

    /// <summary>
    /// 将Unity原生的警告和错误类日志仅写入文件 避免重新发送到控制台造成递归或重复
    /// </summary>
    private void HandleUnityLog(string message, string stackTrace, LogType type)
    {
        if (t_isForwarding || Volatile.Read(ref _isDisposed) != 0) return;
        if (type == LogType.Log) return;
        lock (_sync)
        {
            if (_isDisposed != 0) return;
            // 仅沿用回调提供的堆栈 不重新采集或补造调用位置
            var diagnostics = string.IsNullOrWhiteSpace(stackTrace) ? "Unity未提供调用堆栈" : stackTrace;
            var entry = FormatEntry("Unity", type, message, diagnostics);
            _unityLogger.ZLog(ToLogLevel(type), $"{entry}", filePath: "", lineNumber: 0);
        }
    }

    /// <summary>
    /// 输出后台写入故障并阻止诊断日志回流到文件提供器
    /// </summary>
    private static void ReportInternalError(Exception exception)
    {
        var wasForwarding = t_isForwarding;
        t_isForwarding = true;
        try
        {
            Debug.LogError($"[日志系统]文件日志处理失败:{exception}");
        }
        finally
        {
            t_isForwarding = wasForwarding;
        }
    }

    /// <summary>
    /// 将Unity日志类型映射到标准日志等级
    /// </summary>
    private static LogLevel ToLogLevel(LogType type) => type switch
    {
        LogType.Warning => LogLevel.Warning,
        LogType.Error or LogType.Exception or LogType.Assert => LogLevel.Error,
        _ => LogLevel.Information,
    };

    /// <summary>
    /// 返回日志等级的中文名称
    /// </summary>
    private static string GetLevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "跟踪",
        LogLevel.Debug => "调试",
        LogLevel.Information => "信息",
        LogLevel.Warning => "警告",
        LogLevel.Error => "错误",
        LogLevel.Critical => "严重",
        _ => "关闭",
    };
}
