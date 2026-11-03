using System;

/// <summary>
/// 本地存档槽位的强类型标识
/// </summary>
public readonly struct SaveSlotId : IEquatable<SaveSlotId>
{
    /// <summary>
    /// 经过非负校验的本地存档槽位编号
    /// </summary>
    public int Id { get; }
    /// <summary>
    /// 默认使用编号为零的本地存档槽位
    /// </summary>
    public static SaveSlotId Default => new SaveSlotId(0);

    /// <summary>
    /// 以非负编号创建存档槽位 非法编号立即报错
    /// </summary>
    public SaveSlotId(int id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
    }

    /// <summary>
    /// 按槽位编号判断是否指向同一个存档
    /// </summary>
    public bool Equals(SaveSlotId other) => Id == other.Id;
    /// <summary>
    /// 按槽位编号判断是否指向同一个存档
    /// </summary>
    public override bool Equals(object obj) => obj is SaveSlotId other && Equals(other);
    /// <summary>
    /// 使用槽位编号生成与相等规则一致的哈希值
    /// </summary>
    public override int GetHashCode() => Id;
    /// <summary>
    /// 生成存档文件使用的带固定编号前缀的槽位名称
    /// </summary>
    public override string ToString() => string.Format("slot_{0:D3}", Id);
    /// <summary>
    /// 比较两个槽位是否具有相同编号
    /// </summary>
    public static bool operator ==(SaveSlotId left, SaveSlotId right) => left.Equals(right);
    /// <summary>
    /// 比较两个槽位是否具有不同编号
    /// </summary>
    public static bool operator !=(SaveSlotId left, SaveSlotId right) => !left.Equals(right);
}
