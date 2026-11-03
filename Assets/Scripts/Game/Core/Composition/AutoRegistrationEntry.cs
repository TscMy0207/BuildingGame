using System;

/// <summary>
/// 单个自动注册类型的描述
/// </summary>
internal class AutoRegistrationEntry
{
    /// <summary>
    /// 实现类型
    /// </summary>
    public Type Type { get; }
    /// <summary>
    /// 是否暴露自身类型
    /// </summary>
    public bool AsSelf { get; }
    /// <summary>
    /// 暴露的接口契约
    /// </summary>
    public Type[] Contracts { get; }

    /// <summary>
    /// 创建注册描述
    /// </summary>
    /// <param name="type">实现类型</param>
    /// <param name="asSelf">是否暴露自身类型</param>
    /// <param name="contracts">暴露的接口契约</param>
    public AutoRegistrationEntry(Type type, bool asSelf, Type[] contracts)
    {
        Type = type;
        AsSelf = asSelf;
        Contracts = contracts ?? Array.Empty<Type>();
    }
}
