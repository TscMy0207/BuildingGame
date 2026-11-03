using System;
using System.Collections.Generic;
using System.Reflection;
using VContainer;

/// <summary>
/// 游戏组合根
/// 统一执行自动注册发现 校验 特殊注册和写入
/// </summary>
public static class GameComposition
{
    /// <summary>
    /// 注册游戏根容器的全部服务
    /// </summary>
    /// <param name="builder">目标容器</param>
    public static void RegisterDefaults(IContainerBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        var entries = AutoRegistration.CreatePlan(builder, typeof(GameComposition).Assembly);
        FrameworkComposition.RegisterSpecial(builder);
        AutoRegistration.Apply(builder, entries);
    }

    /// <summary>
    /// 获取参与自动注册的类型 用于构建时生成保留清单
    /// </summary>
    /// <param name="assembly">待扫描的程序集</param>
    /// <returns>按类型全名排序的类型</returns>
    public static IReadOnlyList<Type> GetAutoRegisteredTypes(Assembly assembly)
    {
        var entries = AutoRegistration.Discover(assembly);
        var types = new List<Type>(entries.Count);
        foreach (var entry in entries) types.Add(entry.Type);
        return types;
    }
}
