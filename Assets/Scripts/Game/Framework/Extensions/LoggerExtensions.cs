using System;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// ILogger通用扩展 自动补全调用者文件与行号
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// 输出普通日志并记录调用位置
    /// </summary>
    public static void Log<T>(this ILogger<T> logger, string message, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        logger.Write(LogType.Log, message, null, callerFile, callerLine);
    }

    /// <summary>
    /// 输出警告日志并记录调用位置
    /// </summary>
    public static void Warning<T>(this ILogger<T> logger, string message, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        logger.Write(LogType.Warning, message, null, callerFile, callerLine);
    }

    /// <summary>
    /// 输出错误日志并记录调用位置
    /// </summary>
    public static void Error<T>(this ILogger<T> logger, string message, [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        logger.Write(LogType.Error, message, null, callerFile, callerLine);
    }

    /// <summary>
    /// 输出错误日志及原始异常并记录调用位置
    /// </summary>
    public static void Error<T>(this ILogger<T> logger, string message, Exception ex,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        logger.Write(LogType.Error, message, ex, callerFile, callerLine);
    }
}
