using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger;
using ZLogger.Providers;
using ZLogger.Unity;

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
    /// 独立的控制台和文件工厂 分别控制格式
    /// </summary>
    private readonly ILoggerFactory _consoleFactory;
    private readonly ILoggerFactory _fileFactory;

    /// <summary>
    /// 仅向文件输出的Unity原生日志分类
    /// </summary>
    private readonly Microsoft.Extensions.Logging.ILogger _unityLogger;

    /// <summary>
    /// 当前日志目录和会话标识 避免不同运行会话共用文件
    /// </summary>
    private readonly string _directory;
    private readonly string _sessionName;
    private int _isDisposed;

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
    /// 创建输出目标 使日志在其他服务启动前即可使用
    /// </summary>
    public LoggingService()
    {
        _directory = Path.Combine(Application.persistentDataPath, "Logs");
        _sessionName = $"BuildingGame-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}";
        _consoleFactory = LoggerFactory.Create(builder =>
        {
            // 底层不设最低等级 输出分流由Write按LogType决定
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddZLoggerUnityDebug(options =>
            {
                options.PrettyStacktrace = false;
                ConfigureFormatter(options);
            });
        });

        try
        {
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
                    ConfigureFormatter(options);
                });
            });
        }
        catch
        {
            _consoleFactory.Dispose();
            throw;
        }

        _unityLogger = _fileFactory.CreateLogger("Unity");
        Application.logMessageReceivedThreaded += HandleUnityLog;
        Application.quitting += Dispose;
    }

    /// <summary>
    /// 配合框架启动流程 日志输出已在构造时就绪
    /// </summary>
    public UniTask StartAsync(StartupContext context) => UniTask.CompletedTask;

    /// <summary>
    /// 按LogType分流 普通日志仅控制台 警告和错误类同时写入文件
    /// </summary>
    public void Write(string category, LogType type, string message, Exception exception, string callerFile, int callerLine)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _isDisposed) != 0) return;

            var logLevel = ToLogLevel(type);
            if (type != LogType.Log)
                _fileFactory.CreateLogger(category).ZLog(logLevel, exception, $"{message}", filePath: callerFile, lineNumber: callerLine);

            var wasForwarding = t_isForwarding;
            t_isForwarding = true;
            try
            {
                var logger = _consoleFactory.CreateLogger(category);
                // Unity提供器遇到Exception会只打印异常 因此在控制台消息中保留完整上下文
                if (exception == null)
                    logger.ZLog(logLevel, $"{message}", filePath: callerFile, lineNumber: callerLine);
                else
                    logger.ZLog(logLevel, $"{message}\n异常信息:{exception}", filePath: callerFile, lineNumber: callerLine);
            }
            finally
            {
                t_isForwarding = wasForwarding;
            }
        }
    }

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
            try
            {
                _fileFactory.Dispose();
            }
            finally
            {
                _consoleFactory.Dispose();
            }
        }
    }

    /// <summary>
    /// 统一输出中文标签以及日志时间 分类 等级和原始调用位置
    /// </summary>
    private static void ConfigureFormatter(ZLoggerOptions options)
    {
        options.UsePlainTextFormatter(formatter =>
        {
            formatter.SetPrefixFormatter($"时间:[{0}] 模块:[{1}] 等级:[{2}] ",
                (in MessageTemplate template, in LogInfo info) =>
                    template.Format(info.Timestamp, info.Category, GetLevelName(info.LogLevel)));
            formatter.SetSuffixFormatter($" 调用位置:[{0}:{1}]",
                (in MessageTemplate template, in LogInfo info) => template.Format(info.FilePath, info.LineNumber));
        });
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
            _unityLogger.ZLog(ToLogLevel(type), $"{message}\n{stackTrace}", filePath: "", lineNumber: 0);
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
