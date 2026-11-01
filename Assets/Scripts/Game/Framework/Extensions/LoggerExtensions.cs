using System;
using System.Runtime.CompilerServices;

/// <summary>
/// ILogger通用扩展 自动补全调用者文件与行号
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// 输出调试日志并记录调用位置
    /// </summary>
    public static void Debug<T>(this ILogger<T> logger, string message, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        if (!logger.IsEnabled(LoggingLevel.Debug)) return;
        Publish(logger, LoggingLevel.Debug, message, null, callerFile, callerLine);
    }

    /// <summary>
    /// 输出信息日志并记录调用位置
    /// </summary>
    public static void Info<T>(this ILogger<T> logger, string message, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        if (!logger.IsEnabled(LoggingLevel.Info)) return;
        Publish(logger, LoggingLevel.Info, message, null, callerFile, callerLine);
    }

    /// <summary>
    /// 输出警告日志并记录调用位置
    /// </summary>
    public static void Warning<T>(this ILogger<T> logger, string message, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        if (!logger.IsEnabled(LoggingLevel.Warning)) return;
        Publish(logger, LoggingLevel.Warning, message, null, callerFile, callerLine);
    }

    /// <summary>
    /// 输出错误日志及原始异常并记录调用位置
    /// </summary>
    public static void Error<T>(this ILogger<T> logger, string message, Exception ex, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        if (!logger.IsEnabled(LoggingLevel.Error)) return;
        Publish(logger, LoggingLevel.Error, message, ex, callerFile, callerLine);
    }

    /// <summary>
    /// 将日志参数转发给框架日志接口
    /// </summary>
    private static void Publish<T>(ILogger<T> logger, LoggingLevel level, string message, Exception ex,
        string callerFile, int callerLine)
    {
        logger.Write(level, message, ex, callerFile, callerLine);
    }
}
