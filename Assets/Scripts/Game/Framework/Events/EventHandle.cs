using System;

/// <summary>
/// 可主动释放或通过AddTo绑定生命周期的事件订阅
/// </summary>
public class EventHandle : LifetimeHandle
{
    /// <summary>
    /// 结束订阅的服务回调
    /// </summary>
    private Action _unsubscribe;
    /// <summary>
    /// 订阅标识 仅在所属事件服务中有效
    /// </summary>
    public int Id { get; }
    /// <summary>
    /// 订阅是否仍然有效
    /// </summary>
    public bool IsValid => !IsDisposed;
    /// <summary>
    /// 绑定消息订阅的唯一清理入口
    /// </summary>
    /// <param name="id">订阅编号</param>
    /// <param name="unsubscribe">服务清理回调</param>
    internal EventHandle(int id, Action unsubscribe) { Id = id; _unsubscribe = unsubscribe; }
    /// <summary>
    /// 解除服务和MessagePipe持有的订阅引用
    /// </summary>
    protected override void OnDispose()
    {
        var unsubscribe = _unsubscribe;
        _unsubscribe = null;
        unsubscribe?.Invoke();
    }
}
