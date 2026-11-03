using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 定时器运行时实例句柄
/// </summary>
public sealed class TimerHandle : LifetimeHandle
{
    /// <summary>取消所属定时器的回调</summary>
    private Action _cancel;
    /// <summary>当前服务内唯一编号</summary>
    public int Id { get; }
    /// <summary>是否尚未结束</summary>
    public bool IsValid => !IsDisposed;
    /// <summary>绑定定时器取消入口</summary>
    /// <param name="id">定时器编号</param>
    /// <param name="cancel">取消回调</param>
    internal TimerHandle(int id, Action cancel) { Id = id; _cancel = cancel; }
    /// <summary>主动取消定时器 自然完成时回调无副作用</summary>
    protected override void OnDispose()
    {
        var cancel = _cancel;
        _cancel = null;
        cancel?.Invoke();
    }
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
    /// <summary>取消当前定时器的令牌源</summary>
    private readonly CancellationTokenSource _cancellation;
    /// <summary>缓存令牌以便令牌源释放后查询状态</summary>
    private readonly CancellationToken _token;
    /// <summary>结束通知回调</summary>
    private readonly Action<TimerHandle> _onCompleted;

    /// <summary>所属定时器句柄</summary>
    public TimerHandle Handle { get; }
    /// <summary>当前定时器的取消令牌</summary>
    public CancellationToken Token => _token;
    /// <summary>是否已经收到取消请求</summary>
    public bool IsCancellationRequested => _token.IsCancellationRequested;

    /// <summary>记录定时器的取消和完成行为</summary>
    public TimerInstance(TimerHandle handle, CancellationTokenSource cancellation, Action<TimerHandle> onCompleted)
    {
        Handle = handle;
        _cancellation = cancellation;
        _token = cancellation.Token;
        _onCompleted = onCompleted;
    }

    /// <summary>请求停止当前定时器</summary>
    public void Cancel()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>释放定时器取消源</summary>
    public void Dispose() => _cancellation.Dispose();
    
    /// <summary>
    /// 通知定时器完成
    /// </summary>
    public void NotifyCompleted() => _onCompleted?.Invoke(Handle);
}

/// <summary>
/// 定时器服务实现
/// </summary>
[AutoRegister(typeof(ITimerService), typeof(IStartupService), AsSelf = false)]
internal class TimerService : ISystem, IStartupService, ITimerService
{
    #region 服务生命周期

    /// <summary>服务启动顺序</summary>
    public int Priority => FrameworkSettings.TimerStartupPriority;
    /// <summary>启动进度说明</summary>
    public string Description => "定时器服务";
    /// <summary>启动进度权重</summary>
    public float Weight => FrameworkSettings.TimerStartupWeight;

    /// <summary>绑定全局服务生命周期</summary>
    public UniTask StartAsync(StartupContext context)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TimerService));
        _lifetimeToken = context.LifetimeToken;
        return UniTask.CompletedTask;
    }

    /// <summary>停止所有尚未结束的定时器</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var handles = new List<TimerHandle>(_instances.Keys);
        for (var i = handles.Count - 1; i >= 0; i--)
            Cancel(handles[i]);
    }

    #endregion

    /// <summary>定时器异常记录器</summary>
    private readonly ILogger<TimerService> _logger;
    /// <summary>仍在运行的定时器实例</summary>
    private readonly Dictionary<TimerHandle, TimerInstance> _instances = new();

    /// <summary>全局服务取消令牌</summary>
    private CancellationToken _lifetimeToken = new(true);
    /// <summary>下一个定时器编号</summary>
    private int _nextHandleId;
    /// <summary>服务是否已经释放</summary>
    private bool _disposed;

    /// <summary>接收定时器日志服务</summary>
    public TimerService(ILogger<TimerService> logger)
    {
        _logger = logger;
    }

    /// <summary>创建一次性定时器并返回可释放句柄</summary>
    public TimerHandle Schedule(TimeSpan delay, DelayType delayType, Action callback, CancellationToken cancellationToken = default, Action<TimerHandle> onCompleted = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TimerService));
        _lifetimeToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), delay, "定时器延迟不能小于零");
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        
        // 创建定时器实例
        var instance = CreateInstance(cancellationToken, onCompleted);
        // 启动一次定时器
        RunOnceAsync(delay, delayType, callback, instance).Forget();
        return instance.Handle;
    }

    /// <summary>创建循环定时器并返回可释放句柄</summary>
    public TimerHandle ScheduleRepeating(TimeSpan interval, DelayType delayType, Action callback, CancellationToken cancellationToken = default, Action<TimerHandle> onCompleted = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TimerService));
        _lifetimeToken.ThrowIfCancellationRequested();
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
        TimerHandle handle = null;
        handle = new TimerHandle(checked(++_nextHandleId), () => Cancel(handle));
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken, cancellationToken);
        var instance = new TimerInstance(handle, linkedCancellation, onCompleted);
        _instances.Add(handle, instance);
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
            _logger.Error("定时器回调执行异常", exception);
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
            _logger.Error("循环定时器回调执行异常", exception);
        }
        finally
        {
            DisposeTimer(instance);
        }
    }

    /// <summary>请求取消指定定时器</summary>
    public void Cancel(TimerHandle handle)
    {
        if (handle == null || !_instances.TryGetValue(handle, out var instance))
            return;

        instance.Cancel();
        handle.Dispose();
    }

    /// <summary>
    /// 释放定时器实例
    /// </summary>
    private void DisposeTimer(TimerInstance instance)
    {
        if (!_instances.Remove(instance.Handle))
            return;

        instance.Dispose();
        instance.Handle.Dispose();

        try
        {
            instance.NotifyCompleted();
        }
        catch (Exception exception)
        {
            _logger.Error("定时器完成通知执行异常", exception);
        }
    }
    
    /// <summary>
    /// 检查定时器是否正在运行
    /// </summary>
    public bool IsActive(TimerHandle handle) => !_disposed && handle != null && handle.IsValid && _instances.TryGetValue(handle, out var instance) && !instance.IsCancellationRequested;
}
