using System;
using Cysharp.Threading.Tasks;

/// <summary>
/// 基础服务生命周期接口
/// </summary>
public interface IStartupService : IDisposable
{
    /// <summary>
    /// 执行优先级 越小越先执行
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 显示在进度条上的描述文字
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 占总进度的权重0到1 所有权重会被基础服务管理器自动归一化
    /// </summary>
    float Weight { get; }

    /// <summary>
    /// 启动基础服务
    /// </summary>
    UniTask StartAsync(StartupContext context);
}
