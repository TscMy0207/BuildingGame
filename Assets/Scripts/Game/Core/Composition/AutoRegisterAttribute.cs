using System;

/// <summary>
/// 标记参与自动注册的具体类
/// 特性不继承 契约通过构造函数显式声明
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class AutoRegisterAttribute : Attribute
{
    /// <summary>
    /// 需要暴露的接口契约
    /// </summary>
    public Type[] Contracts { get; }
    /// <summary>
    /// 是否同时暴露自身类型
    /// </summary>
    public bool AsSelf { get; set; } = true;

    /// <summary>
    /// 创建自动注册声明
    /// </summary>
    /// <param name="contracts">需要暴露的接口契约</param>
    public AutoRegisterAttribute(params Type[] contracts)
    {
        Contracts = contracts ?? Array.Empty<Type>();
    }
}
