using System;
using System.Collections.Generic;

/// <summary>
/// 模块注册
/// </summary>
public interface IModuleCatalog
{
    void RegisterModule<TModule>(ModuleKey moduleKey) where TModule : ScopeModule,new();
}

/// <summary>
/// 模块注册表 负责记录模块键与模块类型的对应关系
/// </summary>
internal sealed class ModuleCatalog : IModuleCatalog
{
    private readonly Dictionary<int,IModuleRegistration> m_registrations = new();

    public void RegisterModule<TModule>(ModuleKey moduleKey) where TModule : ScopeModule,new()
    {
        if (moduleKey == null) throw new ArgumentNullException(nameof(moduleKey));

        m_registrations[moduleKey.Id] = new ModuleRegistration<TModule>(moduleKey);
    }

    internal bool TryGetRegistration(int key, out IModuleRegistration registration)
    {
        return m_registrations.TryGetValue(key, out registration);
    }
}

internal interface IModuleRegistration
{
    int Id { get; }

    string Name { get; }
    
    ScopeModule CreateModule();
}

internal sealed class ModuleRegistration<TModule> : IModuleRegistration where TModule : ScopeModule,new()
{
    private readonly ModuleKey m_moduleKey;

    public int Id => m_moduleKey.Id;
    public string Name => m_moduleKey.Name;
    public ModuleRegistration(ModuleKey moduleKey)
    {
        m_moduleKey = moduleKey;
    }

    public ScopeModule CreateModule()
    {
        return new TModule();
    }
}

/// <summary>
/// 模块键值
/// </summary>
public abstract class ModuleKey
{
    /// <summary>
    /// 模块注册标识
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// 模块名称 用于日志和调试
    /// </summary>
    public string Name { get; }

    protected ModuleKey(int id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>
/// 无打开参数的模块参数类型
/// </summary>
public readonly struct ModuleEmptyArgs
{
    public static readonly ModuleEmptyArgs Value = default;
}

/// <summary>
/// 带模块参数类型的键值
/// </summary>
/// <typeparam name="TArgs">模块参数类型</typeparam>
public sealed class ModuleKey<TArgs> : ModuleKey
{
    public ModuleKey(int id, string name) : base(id, name)
    {
    }
}
