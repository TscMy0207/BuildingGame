using System;

/// <summary>
/// 当前模块实例的只读运行时上下文
/// </summary>
public interface IModuleContext
{
    /// <summary>
    /// 当前模块实例的句柄
    /// </summary>
    ModuleHandle ModuleHandle { get; }
    
    /// <summary>
    /// 获取当前模块实例的数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <returns>数据</returns>
    T GetData<T>();
}

/// <summary>
/// 单个模块实例的内部运行时记录
/// </summary>
internal sealed class ModuleContext<TArgs> : IModuleContext
{
    public ModuleHandle ModuleHandle { get; }
    private readonly TArgs m_data;

    public ModuleContext(ModuleHandle handle, TArgs data)
    {
        ModuleHandle = handle;
        m_data = data;
    }

    public T GetData<T>()
    {
        if (m_data is T typedData)
            return typedData;

        throw new InvalidOperationException($"模块实例{ModuleHandle}没有数据{typeof(T).Name}");
    }
}

/// <summary>
/// 当前进程内唯一的模块实例标识
/// </summary>
public readonly struct ModuleHandle : IEquatable<ModuleHandle>
{
    /// <summary>
    /// 无效的模块句柄
    /// </summary>
    public static ModuleHandle None => default;

    /// <summary>
    /// 模块注册标识
    /// </summary>
    public int ModuleId { get; }

    /// <summary>
    /// 模块实例标识
    /// </summary>
    public int InstanceId { get; }

    /// <summary>
    /// 是否为有效句柄
    /// </summary>
    public bool IsValid => InstanceId > 0;

    public ModuleHandle(int moduleId, int instanceId)
    {
        ModuleId = moduleId;
        InstanceId = instanceId;
    }

    public bool Equals(ModuleHandle other) => ModuleId == other.ModuleId && InstanceId == other.InstanceId;
    public override bool Equals(object obj)  => obj is ModuleHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ModuleId, InstanceId);
    public override string ToString() => IsValid ? $"{ModuleId}:{InstanceId}" : "None";
}
