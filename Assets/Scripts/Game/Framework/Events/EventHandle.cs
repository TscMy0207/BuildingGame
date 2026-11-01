using System;

/// <summary>
/// 事件订阅句柄
/// </summary>
public readonly struct EventHandle : IEquatable<EventHandle>
{
    /// <summary>
    /// 句柄标识
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// 是否为有效句柄
    /// </summary>
    public bool IsValid => Id > 0;

    public EventHandle(int id)
    {
        Id = id;
    }

    public bool Equals(EventHandle other) => Id == other.Id;
    public override bool Equals(object obj) => obj is EventHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => IsValid ? Id.ToString() : "None";

    public static bool operator ==(EventHandle left, EventHandle right) => left.Equals(right);
    public static bool operator !=(EventHandle left, EventHandle right) => !left.Equals(right);
}
