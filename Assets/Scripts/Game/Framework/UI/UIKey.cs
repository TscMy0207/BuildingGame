/// <summary>
/// 带参数类型的UI键值
/// </summary>
/// <typeparam name="TArgs">UI打开参数类型</typeparam>
public class UIKey<TArgs>
{
    /// <summary>
    /// UI标识
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// UI名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 关联稳定面板标识与便于诊断的面板名称
    /// </summary>
    public UIKey(int id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>
/// 无打开参数的UI参数类型
/// </summary>
public readonly struct UIEmptyArgs
{
    /// <summary>
    /// 无需打开参数的面板请求复用的空参数值
    /// </summary>
    public static readonly UIEmptyArgs Value = default;
}
