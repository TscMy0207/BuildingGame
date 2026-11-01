using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// 作用域定时器服务
/// </summary>
public interface IScopeTimerService
{
    /// <summary>
    /// 在作用域内调度一个定时器
    /// </summary>
    TimerHandle Schedule(TimeSpan delay, DelayType delayType, Action callback);

    /// <summary>
    /// 在作用域内调度一个重复定时器
    /// </summary>
    TimerHandle ScheduleRepeating(TimeSpan interval, DelayType delayType, Action callback);

    /// <summary>
    /// 取消当前作用域创建的定时器
    /// </summary>
    void Cancel(TimerHandle handle);

    /// <summary>
    /// 检查当前作用域创建的定时器是否正在运行
    /// </summary>
    bool IsActive(TimerHandle handle);
}

/// <summary>
/// 作用域定时器服务实现
/// </summary>
internal sealed class ScopeTimerService : IScopeTimerService, IDisposable
{
    private readonly ITimerService m_timerService;
    private readonly IScopeModule m_scopeModule;
    private readonly List<TimerHandle> m_createdTimers = new();

    public ScopeTimerService(ITimerService timerService, IScopeModule scopeModule)
    {
        m_timerService = timerService;
        m_scopeModule = scopeModule;
    }

    public TimerHandle Schedule(TimeSpan delay, DelayType delayType, Action callback)
    {
        var lifetimeToken = m_scopeModule.LifetimeToken;
        lifetimeToken.ThrowIfCancellationRequested();

        var handle = m_timerService.Schedule(delay, delayType, callback, lifetimeToken, OnTimerCompleted);
        m_createdTimers.Add(handle);
        return handle;
    }

    public TimerHandle ScheduleRepeating(TimeSpan interval, DelayType delayType, Action callback)
    {
        var lifetimeToken = m_scopeModule.LifetimeToken;
        lifetimeToken.ThrowIfCancellationRequested();

        var handle = m_timerService.ScheduleRepeating(interval, delayType, callback, lifetimeToken, OnTimerCompleted);
        m_createdTimers.Add(handle);
        return handle;
    }

    public void Cancel(TimerHandle handle)
    {
        if (!m_createdTimers.Remove(handle)) return;
        m_timerService.Cancel(handle);
    }
    
    public void Dispose()
    {
        var handles = new List<TimerHandle>(m_createdTimers);
        m_createdTimers.Clear();

        for (var i = handles.Count - 1; i >= 0; i--)
            m_timerService.Cancel(handles[i]);
    }
    
    public bool IsActive(TimerHandle handle) => m_createdTimers.Contains(handle) && m_timerService.IsActive(handle);

    /// <summary>
    /// 定时器完成时调用 
    /// </summary>
    /// <param name="handle">已完成的定时器句柄</param>
    private void OnTimerCompleted(TimerHandle handle) => m_createdTimers.Remove(handle);
}
