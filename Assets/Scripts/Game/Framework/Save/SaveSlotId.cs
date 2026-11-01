using System;

/// <summary>
/// 本地存档槽位的强类型标识。
/// </summary>
public readonly struct SaveSlotId : IEquatable<SaveSlotId>
{
    public int Id { get; }
    public static SaveSlotId Default => new SaveSlotId(0);

    public SaveSlotId(int id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
    }

    public bool Equals(SaveSlotId other) => Id == other.Id;
    public override bool Equals(object obj) => obj is SaveSlotId other && Equals(other);
    public override int GetHashCode() => Id;
    public override string ToString() => string.Format("slot_{0:D3}", Id);
    public static bool operator ==(SaveSlotId left, SaveSlotId right) => left.Equals(right);
    public static bool operator !=(SaveSlotId left, SaveSlotId right) => !left.Equals(right);
}
