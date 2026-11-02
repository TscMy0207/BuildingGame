using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;

/// <summary>
/// 全局事件服务接口
/// </summary>
public interface IEventService
{
    /// <summary>
    /// 发布事件
    /// </summary>
    void Publish<TArgs>(EventKey<TArgs> key, TArgs args);

    /// <summary>
    /// 订阅事件
    /// </summary>
    EventHandle Subscribe<TArgs>(EventKey<TArgs> key, Action<TArgs> handler);

    /// <summary>
    /// 解除事件订阅
    /// </summary>
    void Unsubscribe(EventHandle handle);

    /// <summary>
    /// 检查事件订阅是否有效
    /// </summary>
    bool IsSubscribed(EventHandle handle);
}

/// <summary>
/// 基于MessagePipe实现全局事件发布与订阅
/// </summary>
internal sealed class EventService : IStartupService, IEventService
{
    /// <summary>
    /// 事件回调异常日志记录器
    /// </summary>
    private readonly ILogger<EventService> _logger;
    /// <summary>
    /// MessagePipe全局消息发布器
    /// </summary>
    private readonly IPublisher<int, object> _publisher;
    /// <summary>
    /// MessagePipe全局消息订阅器
    /// </summary>
    private readonly ISubscriber<int, object> _subscriber;
    /// <summary>
    /// 事件句柄到MessagePipe订阅释放器的映射
    /// </summary>
    private readonly Dictionary<int, EventHandle> _subscriptions = new();
    /// <summary>
    /// 事件标识对应的参数类型
    /// </summary>
    private readonly Dictionary<int, Type> _argumentTypes = new();
    /// <summary>
    /// 基础事件服务生命周期令牌
    /// </summary>
    private CancellationToken _lifetimeToken = new(true);
    /// <summary>
    /// 下一个事件订阅句柄编号
    /// </summary>
    private int _nextHandleId;
    /// <summary>服务是否已经释放</summary>
    private bool _disposed;

    /// <summary>
    /// 创建全局事件服务
    /// </summary>
    /// <param name="logger">事件回调异常日志记录器</param>
    /// <param name="publisher">MessagePipe全局消息发布器</param>
    /// <param name="subscriber">MessagePipe全局消息订阅器</param>
    public EventService(
        ILogger<EventService> logger,
        IPublisher<int, object> publisher,
        ISubscriber<int, object> subscriber)
    {
        _logger = logger;
        _publisher = publisher;
        _subscriber = subscriber;
    }

    /// <summary>
    /// 事件服务启动优先级
    /// </summary>
    public int Priority => FrameworkSettings.EventStartupPriority;

    /// <summary>
    /// 事件服务启动描述
    /// </summary>
    public string Description => "事件服务";

    /// <summary>
    /// 事件服务启动进度权重
    /// </summary>
    public float Weight => FrameworkSettings.EventStartupWeight;

    /// <summary>
    /// 绑定事件服务生命周期
    /// </summary>
    /// <param name="context">基础服务启动上下文</param>
    /// <returns>已完成的启动任务</returns>
    public UniTask StartAsync(StartupContext context)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EventService));
        context.LifetimeToken.ThrowIfCancellationRequested();
        _lifetimeToken = context.LifetimeToken;
        return UniTask.CompletedTask;
    }

    /// <summary>
    /// 发布事件并同步通知所有订阅者
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="args">事件参数</param>
    /// <typeparam name="TArgs">事件参数类型</typeparam>
    public void Publish<TArgs>(EventKey<TArgs> key, TArgs args)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EventService));
        _lifetimeToken.ThrowIfCancellationRequested();
        EnsureArgumentType(key);
        _publisher.Publish(key.Id, args);
    }

    /// <summary>
    /// 订阅事件并返回用于取消订阅的句柄
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="handler">事件处理函数</param>
    /// <typeparam name="TArgs">事件参数类型</typeparam>
    /// <returns>事件订阅句柄</returns>
    public EventHandle Subscribe<TArgs>(EventKey<TArgs> key, Action<TArgs> handler)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EventService));
        _lifetimeToken.ThrowIfCancellationRequested();
        EnsureArgumentType(key);

        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var id = checked(++_nextHandleId);
        EventHandle handle = null;
        var subscription = _subscriber.Subscribe(key.Id, args =>
        {
            try
            {
                if (handle != null && handle.IsValid && !_disposed && !_lifetimeToken.IsCancellationRequested) handler((TArgs)args);
            }
            catch (Exception exception)
            {
                _logger.Error($"执行事件{key.Id}的订阅回调时出现异常", exception);
            }
        });

        handle = new EventHandle(id, () => { _subscriptions.Remove(id); subscription.Dispose(); });
        _subscriptions.Add(id, handle);
        return handle;
    }

    /// <summary>
    /// 根据句柄取消事件订阅
    /// </summary>
    /// <param name="handle">事件订阅句柄</param>
    public void Unsubscribe(EventHandle handle)
    {
        if (handle != null && _subscriptions.TryGetValue(handle.Id, out var owned) && ReferenceEquals(owned, handle))
            handle.Dispose();
    }

    /// <summary>
    /// 检查事件订阅句柄是否仍有效
    /// </summary>
    /// <param name="handle">事件订阅句柄</param>
    /// <returns>订阅是否仍然有效</returns>
    public bool IsSubscribed(EventHandle handle)
    {
        return !_disposed && handle != null && handle.IsValid && !_lifetimeToken.IsCancellationRequested &&
            _subscriptions.TryGetValue(handle.Id, out var owned) && ReferenceEquals(owned, handle);
    }

    /// <summary>
    /// 检查同一事件标识是否始终使用相同的参数类型
    /// </summary>
    /// <param name="key">事件键</param>
    /// <typeparam name="TArgs">事件参数类型</typeparam>
    private void EnsureArgumentType<TArgs>(EventKey<TArgs> key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (_argumentTypes.TryGetValue(key.Id, out var argumentType))
        {
            if (argumentType != typeof(TArgs))
                throw new InvalidOperationException(
                    $"事件{key.Id}已绑定参数类型{argumentType.Name}不能使用{typeof(TArgs).Name}");
            return;
        }

        _argumentTypes.Add(key.Id, typeof(TArgs));
    }

    /// <summary>
    /// 释放所有MessagePipe订阅并清理事件类型记录
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var subscription in new List<EventHandle>(_subscriptions.Values))
            subscription.Dispose();

        _subscriptions.Clear();
        _argumentTypes.Clear();
    }
}
