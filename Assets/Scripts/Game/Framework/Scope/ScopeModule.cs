using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;

/// <summary>
/// 模块实例的生命周期提供者
/// Scope内各服务通过它获取模块取消令牌
/// </summary>
public interface IScopeModule
{
    /// <summary>
    /// 模块生命周期取消令牌
    /// </summary>
    CancellationToken LifetimeToken { get; }
}

/// <summary>
/// 模块基类
/// 子类在OnConfigure注册依赖 在OnStartAsync和OnStopAsync处理启停
/// </summary>
public abstract class ScopeModule : IScopeModule
{
    public CancellationToken LifetimeToken { get; }

    internal IModuleContext Context { get; set; }
    internal IScopedObjectResolver Scope { get; set; }

    /// <summary>
    /// 模块生命周期取消令牌
    /// </summary>
    private CancellationTokenSource m_lifetimeCancellation = new();

    protected ScopeModule()
    {
        LifetimeToken = m_lifetimeCancellation.Token;
    }
    
    internal void Configure(IContainerBuilder builder)
    {
        builder.Register<ScopeUIService>(Lifetime.Scoped).As<IScopeUIService>();
        builder.Register<ScopeResourcesService>(Lifetime.Scoped).As<IScopeResourcesService>();
        builder.Register<ScopeObjectPoolService>(Lifetime.Scoped).As<IScopeObjectPoolService>();
        builder.Register<ScopeModuleService>(Lifetime.Scoped).As<IScopeModuleService>();
        builder.Register<ScopeTimerService>(Lifetime.Scoped).As<IScopeTimerService>();
        builder.Register<ScopeEventService>(Lifetime.Scoped).As<IScopeEventService>();
        OnConfigure(builder);
    }
    protected abstract void OnConfigure(IContainerBuilder builder);

    internal UniTask StartAsync()
    {
        return OnStartAsync();
    }
    protected abstract UniTask OnStartAsync();

    internal async UniTask StopAsync()
    {
        var lifetimeCancellation = m_lifetimeCancellation;
        if (lifetimeCancellation == null || lifetimeCancellation.IsCancellationRequested)
            return;

        try
        {
            // 停止顺序 先取消模块生命周期 再关闭Scope内UI
            // 最后执行模块自身的停止逻辑
            lifetimeCancellation.Cancel();

            if (Scope == null) return;
            await Scope.Resolve<IScopeUIService>().CloseAllAsync();

            // 该过程中可能已被外部释放 需要二次确认
            if (m_lifetimeCancellation == null) return;
            await OnStopAsync();
        }
        finally
        {
            Dispose();
        }
    }
    protected abstract UniTask OnStopAsync();
    
    internal void Dispose()
    {
        var lifetimeCancellation = m_lifetimeCancellation;
        if (lifetimeCancellation == null) return;
        m_lifetimeCancellation = null;

        lifetimeCancellation.Cancel();
        Scope?.Dispose();
        lifetimeCancellation.Dispose();
        OnDispose();
    }
    protected abstract void OnDispose();
    
    public T GetData<T>() => Context.GetData<T>();
}
