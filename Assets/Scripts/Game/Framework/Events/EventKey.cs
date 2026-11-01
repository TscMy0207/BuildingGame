using System;

/// <summary>
/// 带参数类型的事件键
/// </summary>
/// <typeparam name="TArgs">事件参数类型</typeparam>
public sealed class EventKey<TArgs>
{
    /// <summary>
    /// 事件标识
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// 事件名称 用于日志和调试
    /// </summary>
    public string Name { get; }

    public EventKey(int id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>
/// 无参数事件的参数类型
/// </summary>
public readonly struct EventEmptyArgs
{
    public static readonly EventEmptyArgs Value = default;
}
