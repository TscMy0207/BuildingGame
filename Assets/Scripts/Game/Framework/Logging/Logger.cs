using System;
using UnityEngine;

/// <summary>
/// 泛型日志接口 通过T自动提取模块名
/// </summary>
public interface ILogger<T>
{
    /// <summary>
    /// 写入一条日志
    /// </summary>
    void Write(LogType type, string message, Exception exception, string callerFile, int callerLine);
}

/// <summary>
/// 保持框架日志调用方式 并将日志交给ZLogger服务
/// </summary>
internal class Logger<T> : ILogger<T>
{
    /// <summary>
    /// 向控制台和文件分发日志的实例服务
    /// </summary>
    private readonly ILoggingService _loggingService;
    /// <summary>
    /// 从泛型参数提取的模块分类名称
    /// </summary>
    private readonly string _category;

    /// <summary>
    /// 绑定日志服务并使用类型名称作为日志分类
    /// </summary>
    public Logger(ILoggingService loggingService)
    {
        _loggingService = loggingService;
        _category = typeof(T).Name;
    }

    /// <summary>
    /// 转发消息 异常和原始调用位置
    /// </summary>
    public void Write(LogType type, string message, Exception exception, string callerFile, int callerLine)
    {
        _loggingService.Write(_category, type, message, exception, callerFile, callerLine);
    }
}
