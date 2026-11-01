using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// 作用域事件服务接口
/// </summary>
public interface IScopeEventService
{
    /// <summary>
    /// 发布事件
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="args">事件参数</param>
    /// <typeparam name="TArgs">事件参数类型</typeparam>
    void Publish<TArgs>(EventKey<TArgs> key, TArgs args);

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="handler">事件处理函数</param>
    /// <typeparam name="TArgs">事件参数类型</typeparam>
    /// <returns>事件句柄</returns>
    EventHandle Subscribe<TArgs>(EventKey<TArgs> key, Action<TArgs> handler);

    /// <summary>
    /// 解除事件订阅
    /// </summary>
    /// <param name="handle">事件句柄</param>
    void Unsubscribe(EventHandle handle);

    /// <summary>
    /// 检查事件订阅是否有效
    /// </summary>
    bool IsSubscribed(EventHandle handle);
}

/// <summary>
/// 作用域事件服务实现
/// </summary>
internal sealed class ScopeEventService : IScopeEventService, IDisposable
{
    private readonly IEventService m_eventService;
    private readonly IScopeModule m_scopeModule;
    private readonly List<EventHandle> m_subscriptions = new();// 事件订阅句柄列表

    public ScopeEventService(IEventService eventService, IScopeModule scopeModule)
    {
        m_eventService = eventService;
        m_scopeModule = scopeModule;
    }

    public void Publish<TArgs>(EventKey<TArgs> key, TArgs args)
    {
        m_scopeModule.LifetimeToken.ThrowIfCancellationRequested();
        m_eventService.Publish(key, args);
    }

    public EventHandle Subscribe<TArgs>(EventKey<TArgs> key, Action<TArgs> handler)
    {
        m_scopeModule.LifetimeToken.ThrowIfCancellationRequested();

        var handle = m_eventService.Subscribe(key, handler);
        m_subscriptions.Add(handle);
        return handle;
    }

    public void Unsubscribe(EventHandle handle)
    {
        if (!m_subscriptions.Remove(handle)) return;
        m_eventService.Unsubscribe(handle);
    }

    public bool IsSubscribed(EventHandle handle)
    {
        return !m_scopeModule.LifetimeToken.IsCancellationRequested &&
               m_subscriptions.Contains(handle) &&
               m_eventService.IsSubscribed(handle);
    }

    public void Dispose()
    {
        // 释放所有事件订阅
        if (m_subscriptions.Count == 0) return;
        var subscriptions = new List<EventHandle>(m_subscriptions);
        m_subscriptions.Clear();
        for (var i = subscriptions.Count - 1; i >= 0; i--)
            m_eventService.Unsubscribe(subscriptions[i]);
    }
}
