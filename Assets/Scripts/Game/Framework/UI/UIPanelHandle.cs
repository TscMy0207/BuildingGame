using System;

/// <summary>
/// UI面板运行时实例句柄
/// </summary>
public readonly struct UIPanelHandle : IEquatable<UIPanelHandle>
{
    /// <summary>
    /// 句柄标识
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// 是否为有效句柄
    /// </summary>
    public bool IsValid => Id > 0;

    public UIPanelHandle(int id) => Id = id;

    public bool Equals(UIPanelHandle other) => Id == other.Id;
    public override bool Equals(object obj) => obj is UIPanelHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => IsValid ? Id.ToString() : "None";

    public static bool operator ==(UIPanelHandle left, UIPanelHandle right) => left.Equals(right);
    public static bool operator !=(UIPanelHandle left, UIPanelHandle right) => !left.Equals(right);
}
