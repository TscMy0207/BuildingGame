using System;
using System.Collections.Generic;
using System.Linq;
using VContainer;

/// <summary>通过反射发现Game程序集中的业务系统与数据对象 并登记到根容器</summary>
public static class CoreComposition
{
    /// <summary>
    /// 按具体类型与业务接口注册共享实例 不主动构造对象或执行初始化
    /// </summary>
    /// <param name="builder">尚未构建的依赖容器</param>
    public static void RegisterDefaults(IContainerBuilder builder)
    {
        RegisterTypes(builder, typeof(CoreComposition).Assembly.GetTypes());
    }

    /// <summary>校验全部候选类型后注册 避免冲突时留下部分注册结果</summary>
    /// <param name="builder">尚未构建的依赖容器</param>
    /// <param name="types">待筛选的类型集合</param>
    internal static void RegisterTypes(IContainerBuilder builder, IEnumerable<Type> types)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (types == null) throw new ArgumentNullException(nameof(types));

        var candidates = types.Where(type => type != null && type.IsClass && !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                (type.IsSubclassOf(typeof(ModuleSystem)) || type.IsSubclassOf(typeof(ModuleModel))))
            .Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        var contracts = new Dictionary<Type, Type>();

        foreach (var type in candidates)
        {
            EnsureAvailable(builder, contracts, type, type);
            foreach (var contract in GetContracts(type))
                EnsureAvailable(builder, contracts, contract, type);
        }

        foreach (var type in candidates)
        {
            var registration = builder.Register(type, Lifetime.Singleton).AsSelf();
            foreach (var contract in GetContracts(type)) registration.As(contract);
        }
    }

    /// <summary>获取用于注入的业务接口 释放接口由容器按实例管理</summary>
    /// <param name="type">业务实现类型</param>
    /// <returns>除IDisposable以外的实现接口</returns>
    private static IEnumerable<Type> GetContracts(Type type) =>
        type.GetInterfaces().Where(contract => contract != typeof(IDisposable));

    /// <summary>拒绝重复实现和已有注册冲突 避免解析结果取决于扫描顺序</summary>
    /// <param name="builder">待注册的容器</param>
    /// <param name="contracts">本批次已登记的类型与接口</param>
    /// <param name="contract">需要暴露的类型或接口</param>
    /// <param name="implementation">提供契约的具体类型</param>
    private static void EnsureAvailable(IContainerBuilder builder, Dictionary<Type, Type> contracts,
        Type contract, Type implementation)
    {
        if (contracts.TryGetValue(contract, out var existing))
            throw new InvalidOperationException($"业务依赖注册冲突:{contract.FullName}同时由{existing.FullName}和{implementation.FullName}提供");
        if (builder.Exists(contract, true, true))
            throw new InvalidOperationException($"业务依赖已注册:{contract.FullName}无法自动注册{implementation.FullName}");
        contracts.Add(contract, implementation);
    }
}
