using System;

/// <summary>可取消的生命周期句柄 仅在Unity主线程使用</summary>
public abstract class LifetimeHandle : IDisposable
{
    /// <summary>句柄结束时通知生命周期集合解除持有</summary>
    internal event Action<LifetimeHandle> Completed;

    /// <summary>句柄是否已经结束</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>结束句柄并解除所有生命周期绑定 重复调用无效</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        try { OnDispose(); }
        finally
        {
            var completed = Completed;
            Completed = null;
            completed?.Invoke(this);
        }
    }

    /// <summary>由具体句柄执行取消或归还操作</summary>
    protected abstract void OnDispose();
}
