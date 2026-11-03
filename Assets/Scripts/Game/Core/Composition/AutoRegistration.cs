using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VContainer;

/// <summary>
/// 发现 校验并注册带AutoRegister特性的类型
/// 校验阶段不写入容器 避免失败时留下部分注册
/// </summary>
internal static class AutoRegistration
{
    /// <summary>
    /// 允许存在多个实现的契约 只通过集合消费
    /// </summary>
    private static readonly HashSet<Type> s_multiContracts = new()
    {
        typeof(IStartupService),
        typeof(IConfigTableSubService),
        typeof(ISaveDataHandler)
    };

    /// <summary>
    /// 扫描程序集并创建已验证的注册计划
    /// </summary>
    /// <param name="builder">目标容器</param>
    /// <param name="assembly">待扫描的程序集</param>
    /// <returns>已验证的注册计划</returns>
    internal static IReadOnlyList<AutoRegistrationEntry> CreatePlan(IContainerBuilder builder, Assembly assembly)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        var entries = Discover(assembly);
        ValidateAgainstBuilder(builder, entries);
        return entries;
    }

    /// <summary>
    /// 将已验证的注册计划写入容器
    /// </summary>
    /// <param name="builder">目标容器</param>
    /// <param name="entries">已验证的注册计划</param>
    internal static void Apply(IContainerBuilder builder, IReadOnlyList<AutoRegistrationEntry> entries)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        foreach (var entry in entries)
        {
            var registration = builder.Register(entry.Type, Lifetime.Singleton);
            if (entry.AsSelf) registration.AsSelf();
            foreach (var contract in entry.Contracts) registration.As(contract);
        }
    }

    /// <summary>
    /// 按指定类型列表完成发现 校验和注册
    /// </summary>
    /// <param name="builder">目标容器</param>
    /// <param name="types">候选类型</param>
    internal static void RegisterTypes(IContainerBuilder builder, IEnumerable<Type> types)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        var entries = Discover(types);
        ValidateAgainstBuilder(builder, entries);
        Apply(builder, entries);
    }

    /// <summary>
    /// 扫描程序集并校验类型级声明
    /// </summary>
    /// <param name="assembly">待扫描的程序集</param>
    /// <returns>按类型全名排序的注册描述</returns>
    internal static IReadOnlyList<AutoRegistrationEntry> Discover(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var messages = exception.LoaderExceptions.Where(error => error != null).Select(error => error.Message);
            throw new InvalidOperationException("读取程序集类型失败:" + string.Join(";", messages), exception);
        }
        return Discover(types);
    }

    /// <summary>
    /// 按指定类型列表校验类型级声明
    /// </summary>
    /// <param name="types">候选类型</param>
    /// <returns>按类型全名排序的注册描述</returns>
    internal static IReadOnlyList<AutoRegistrationEntry> Discover(IEnumerable<Type> types)
    {
        if (types == null) throw new ArgumentNullException(nameof(types));
        var errors = new List<string>();
        var entries = new List<AutoRegistrationEntry>();
        foreach (var type in types.Where(type => type != null).Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var attribute = type.GetCustomAttribute<AutoRegisterAttribute>(false);
            if (attribute == null) continue;
            if (!ValidateType(type, attribute, errors)) continue;
            entries.Add(new AutoRegistrationEntry(type, attribute.AsSelf, attribute.Contracts));
        }
        ThrowIfErrors(errors, "自动注册声明无效");
        return entries;
    }

    /// <summary>
    /// 校验类型是否满足自动注册约束
    /// </summary>
    /// <param name="type">待校验类型</param>
    /// <param name="attribute">自动注册声明</param>
    /// <param name="errors">收集错误信息</param>
    /// <returns>是否通过校验</returns>
    internal static bool ValidateType(Type type, AutoRegisterAttribute attribute, List<string> errors)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (attribute == null) throw new ArgumentNullException(nameof(attribute));
        if (errors == null) throw new ArgumentNullException(nameof(errors));

        var before = errors.Count;
        if (!type.IsClass) errors.Add($"自动注册类型必须是类:{type.FullName}");
        if (type.IsAbstract) errors.Add($"自动注册类型不能是抽象类:{type.FullName}");
        if (type.ContainsGenericParameters) errors.Add($"自动注册类型不能是开放泛型:{type.FullName}");
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) errors.Add($"自动注册类型不能继承UnityEngine.Object:{type.FullName}");
        if (!typeof(ISystem).IsAssignableFrom(type) && !typeof(IModel).IsAssignableFrom(type))
            errors.Add($"自动注册类型必须实现ISystem或IModel:{type.FullName}");

        var contracts = attribute.Contracts ?? Array.Empty<Type>();
        if (!attribute.AsSelf && contracts.Length == 0)
            errors.Add($"关闭自身暴露时必须声明契约:{type.FullName}");

        var seen = new HashSet<Type>();
        foreach (var contract in contracts)
        {
            if (contract == null)
            {
                errors.Add($"契约不能为空:{type.FullName}");
                continue;
            }
            if (!contract.IsInterface) errors.Add($"契约必须是接口:{type.FullName} 契约:{contract.FullName}");
            if (contract.ContainsGenericParameters) errors.Add($"契约不能是开放泛型:{type.FullName} 契约:{contract.FullName}");
            if (contract == typeof(ISystem) || contract == typeof(IModel) || contract == typeof(IDisposable))
                errors.Add($"禁止暴露标记接口或释放接口:{type.FullName} 契约:{contract.FullName}");
            if (contract == type) errors.Add($"契约不能是自身类型:{type.FullName}");
            if (!contract.IsAssignableFrom(type)) errors.Add($"类型未实现契约:{type.FullName} 契约:{contract.FullName}");
            if (!seen.Add(contract)) errors.Add($"契约重复声明:{type.FullName} 契约:{contract.FullName}");
        }
        return errors.Count == before;
    }

    /// <summary>
    /// 对照已有注册检查类型和契约冲突
    /// </summary>
    /// <param name="builder">目标容器</param>
    /// <param name="entries">注册描述</param>
    private static void ValidateAgainstBuilder(IContainerBuilder builder, IReadOnlyList<AutoRegistrationEntry> entries)
    {
        var errors = new List<string>();
        var contracts = new Dictionary<Type, List<Type>>();
        foreach (var entry in entries)
        {
            if (builder.Exists(entry.Type, false, false))
                errors.Add($"自动注册类型已存在:{entry.Type.FullName}");
            if (entry.AsSelf) AddContract(contracts, entry.Type, entry.Type);
            foreach (var contract in entry.Contracts)
            {
                AddContract(contracts, contract, entry.Type);
                if (!s_multiContracts.Contains(contract) && builder.Exists(contract, true, false))
                    errors.Add($"契约已存在:{contract.FullName} 无法自动注册{entry.Type.FullName}");
            }
        }

        foreach (var pair in contracts)
        {
            if (pair.Value.Count <= 1 || s_multiContracts.Contains(pair.Key)) continue;
            var implementations = string.Join(",", pair.Value.Select(type => type.FullName));
            errors.Add($"契约存在多个实现:{pair.Key.FullName} 由{implementations}提供");
        }
        ThrowIfErrors(errors, "自动注册校验失败");
    }

    /// <summary>
    /// 记录契约与实现的对应关系
    /// </summary>
    /// <param name="contracts">契约表</param>
    /// <param name="contract">契约类型</param>
    /// <param name="implementation">实现类型</param>
    private static void AddContract(Dictionary<Type, List<Type>> contracts, Type contract, Type implementation)
    {
        if (!contracts.TryGetValue(contract, out var implementations))
        {
            implementations = new List<Type>();
            contracts.Add(contract, implementations);
        }
        implementations.Add(implementation);
    }

    /// <summary>
    /// 在存在错误时一次性报告全部问题
    /// </summary>
    /// <param name="errors">错误信息</param>
    /// <param name="title">错误标题</param>
    private static void ThrowIfErrors(List<string> errors, string title)
    {
        if (errors.Count == 0) return;
        throw new InvalidOperationException(title + ":" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}
