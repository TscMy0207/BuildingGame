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

    /// <summary>
    /// 以服务分配的运行时编号创建面板实例句柄
    /// </summary>
    public UIPanelHandle(int id) => Id = id;

    /// <summary>
    /// 按运行时编号判断是否引用同一面板实例
    /// </summary>
    public bool Equals(UIPanelHandle other) => Id == other.Id;
    /// <summary>
    /// 按运行时编号判断是否引用同一面板实例
    /// </summary>
    public override bool Equals(object obj) => obj is UIPanelHandle other && Equals(other);
    /// <summary>
    /// 由运行时编号生成与相等规则一致的哈希值
    /// </summary>
    public override int GetHashCode() => Id.GetHashCode();
    /// <summary>
    /// 返回有效面板编号 无效句柄显示为None
    /// </summary>
    public override string ToString() => IsValid ? Id.ToString() : "None";

    /// <summary>
    /// 比较两个句柄是否指向同一面板实例
    /// </summary>
    public static bool operator ==(UIPanelHandle left, UIPanelHandle right) => left.Equals(right);
    /// <summary>
    /// 比较两个句柄是否指向不同面板实例
    /// </summary>
    public static bool operator !=(UIPanelHandle left, UIPanelHandle right) => !left.Equals(right);
}
