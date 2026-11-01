using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 定时器运行时实例句柄
/// </summary>
public readonly struct TimerHandle : IEquatable<TimerHandle>
{
    /// <summary>
    /// 句柄标识
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// 是否为有效句柄
    /// </summary>
    public bool IsValid => Id > 0;

    public TimerHandle(int id) => Id = id;

    public bool Equals(TimerHandle other) => Id == other.Id;
    public override bool Equals(object obj) => obj is TimerHandle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => IsValid ? Id.ToString() : "None";

    public static bool operator ==(TimerHandle left, TimerHandle right) => left.Equals(right);
    public static bool operator !=(TimerHandle left, TimerHandle right) => !left.Equals(right);
}

/// <summary>
/// 定时器服务接口
/// </summary>
public interface ITimerService
{
    /// <summary>
    /// 调度一个一次性定时器
    /// 取消令牌触发后回调不会再执行 但onCompleted仍会触发一次
    /// 回调内部异常会被捕获并记录 不会向调用方抛出
    /// </summary>
    /// <param name="delay">延迟时间</param>
    /// <param name="delayType">延迟类型</param>
    /// <param name="callback">到点回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onCompleted">完成通知 正常完成和取消都会触发</param>
    TimerHandle Schedule(TimeSpan delay, DelayType delayType, Action callback, CancellationToken cancellationToken = default, Action<TimerHandle> onCompleted = null);

    /// <summary>
    /// 调度一个重复定时器
    /// 取消令牌触发后回调不会再执行 但onCompleted仍会触发一次
    /// 回调内部异常会被捕获并记录 不会向调用方抛出
    /// </summary>
    /// <param name="interval">重复间隔 必须大于零</param>
    /// <param name="delayType">延迟类型</param>
    /// <param name="callback">每次间隔回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onCompleted">完成通知 正常完成和取消都会触发</param>
    TimerHandle ScheduleRepeating(TimeSpan interval, DelayType delayType, Action callback, CancellationToken cancellationToken = default, Action<TimerHandle> onCompleted = null);

    /// <summary>
    /// 取消指定定时器
    /// 已完成的定时器不会被重复处理
    /// </summary>
    /// <param name="handle">定时器句柄</param>
    void Cancel(TimerHandle handle);

    /// <summary>
    /// 检查指定定时器是否正在运行
    /// </summary>
    /// <param name="handle">定时器句柄</param>
    bool IsActive(TimerHandle handle);
}

/// <summary>
/// 定时器运行时实例
/// </summary>
internal sealed class TimerInstance : IDisposable
{
    private readonly CancellationTokenSource m_cancellation;
    private readonly CancellationToken m_token;
    private readonly Action<TimerHandle> m_onCompleted;

    public TimerHandle Handle { get; }
    public CancellationToken Token => m_token;
    public bool IsCancellationRequested => m_token.IsCancellationRequested;

    public TimerInstance(TimerHandle handle, CancellationTokenSource cancellation, Action<TimerHandle> onCompleted)
    {
        Handle = handle;
        m_cancellation = cancellation;
        m_token = cancellation.Token;
        m_onCompleted = onCompleted;
    }

    public void Cancel()
    {
        if (m_cancellation.IsCancellationRequested)
            return;

        try
        {
            m_cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => m_cancellation.Dispose();
    
    /// <summary>
    /// 通知定时器完成
    /// </summary>
    public void NotifyCompleted() => m_onCompleted?.Invoke(Handle);
}

/// <summary>
/// 定时器服务实现
/// </summary>
internal sealed class TimerService : IStartupService, ITimerService
{
    #region 服务生命周期

    public int Priority => FrameworkSettings.TimerStartupPriority;
    public string Description => "定时器服务";
    public float Weight => FrameworkSettings.TimerStartupWeight;

    public UniTask StartAsync(StartupContext context)
    {
        m_lifetimeToken = context.LifetimeToken;
        return UniTask.CompletedTask;
    }

    public void Dispose()
    {
        var handles = new List<TimerHandle>(m_instances.Keys);
        for (var i = handles.Count - 1; i >= 0; i--)
            Cancel(handles[i]);
    }

    #endregion

    private readonly ILogger<TimerService> m_logger;
    private readonly Dictionary<TimerHandle, TimerInstance> m_instances = new();

    private CancellationToken m_lifetimeToken = new(true);
    private int m_nextHandleId;

    public TimerService(ILogger<TimerService> logger)
    {
        m_logger = logger;
    }

    public TimerHandle Schedule(TimeSpan delay, DelayType delayType, Action callback, CancellationToken cancellationToken, Action<TimerHandle> onCompleted = null)
    {
        m_lifetimeToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), delay, "定时器延迟不能小于零");
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        
        // 创建定时器实例
        var instance = CreateInstance(cancellationToken, onCompleted);
        // 启动一次定时器
        RunOnceAsync(delay, delayType, callback, instance).Forget();
        return instance.Handle;
    }

    public TimerHandle ScheduleRepeating(TimeSpan interval, DelayType delayType, Action callback, CancellationToken cancellationToken, Action<TimerHandle> onCompleted = null)
    {
        m_lifetimeToken.ThrowIfCancellationRequested();
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), interval, "循环定时器间隔必须大于零");
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        cancellationToken.ThrowIfCancellationRequested();
        
        // 创建定时器实例
        var instance = CreateInstance(cancellationToken, onCompleted);
        // 启动重复定时器
        RunRepeatingAsync(interval, delayType, callback, instance).Forget();
        return instance.Handle;
    }
    
    /// <summary>
    /// 创建一个定时器实例
    /// </summary>
    private TimerInstance CreateInstance(CancellationToken cancellationToken, Action<TimerHandle> onCompleted)
    {
        var handle = new TimerHandle(checked(++m_nextHandleId));
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetimeToken, cancellationToken);
        var instance = new TimerInstance(handle, linkedCancellation, onCompleted);
        m_instances.Add(handle, instance);
        return instance;
    }
    
    /// <summary>
    /// 启动一次定时器
    /// </summary>
    private async UniTask RunOnceAsync(TimeSpan delay, DelayType delayType, Action callback, TimerInstance instance)
    {
        try
        {
            await UniTask.Delay(delay, delayType, PlayerLoopTiming.Update, instance.Token);
            instance.Token.ThrowIfCancellationRequested();
            callback.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            m_logger.Error("定时器回调执行异常", exception);
        }
        finally
        {
            DisposeTimer(instance);
        }
    }

    /// <summary>
    /// 启动重复定时器
    /// </summary>
    private async UniTask RunRepeatingAsync(TimeSpan interval, DelayType delayType, Action callback, TimerInstance instance)
    {
        try
        {
            while (!instance.Token.IsCancellationRequested)
            {
                await UniTask.Delay(interval, delayType, PlayerLoopTiming.Update, instance.Token);
                instance.Token.ThrowIfCancellationRequested();
                callback.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            m_logger.Error("循环定时器回调执行异常", exception);
        }
        finally
        {
            DisposeTimer(instance);
        }
    }

    public void Cancel(TimerHandle handle)
    {
        if (!m_instances.TryGetValue(handle, out var instance))
            return;

        instance.Cancel();
    }

    /// <summary>
    /// 释放定时器实例
    /// </summary>
    private void DisposeTimer(TimerInstance instance)
    {
        if (!m_instances.Remove(instance.Handle))
            return;

        instance.Dispose();

        try
        {
            instance.NotifyCompleted();
        }
        catch (Exception exception)
        {
            m_logger.Error("定时器完成通知执行异常", exception);
        }
    }
    
    /// <summary>
    /// 检查定时器是否正在运行
    /// </summary>
    public bool IsActive(TimerHandle handle) => m_instances.TryGetValue(handle, out var instance) && !instance.IsCancellationRequested;
}
