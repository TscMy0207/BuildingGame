using System;
using System.Collections.Generic;

/// <summary>集中管理订阅与任务 Clear后可复用 Dispose后立即释放新加入对象 仅在Unity主线程使用</summary>
public sealed class DisposableBag : IDisposable
{
    /// <summary>当前仍需清理的对象</summary>
    private readonly HashSet<IDisposable> _items = new();

    /// <summary>集合是否已经永久释放</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>尚未结束的对象数量</summary>
    public int Count => _items.Count;

    /// <summary>登记对象 已结束的句柄不会被继续持有</summary>
    /// <param name="item">需要清理的对象</param>
    public void Add(IDisposable item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (ReferenceEquals(item, this)) throw new ArgumentException("释放集合不能持有自身", nameof(item));
        if (IsDisposed) { item.Dispose(); return; }
        if (item is LifetimeHandle handle && handle.IsDisposed) return;
        if (_items.Add(item) && item is LifetimeHandle tracked) tracked.Completed += RemoveCompleted;
    }

    /// <summary>解除已结束句柄的集合引用</summary>
    /// <param name="handle">已经结束的句柄</param>
    private void RemoveCompleted(LifetimeHandle handle)
    {
        _items.Remove(handle);
        handle.Completed -= RemoveCompleted;
    }

    /// <summary>清理当前内容并保留集合供下次打开面板或借出对象时使用</summary>
    public void Clear()
    {
        var items = new List<IDisposable>(_items);
        _items.Clear();
        List<Exception> errors = null;
        foreach (var item in items)
        {
            if (item is LifetimeHandle handle) handle.Completed -= RemoveCompleted;
            try { item.Dispose(); }
            catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
        }
        if (errors != null) throw new AggregateException("清理生命周期对象时出现异常", errors);
    }

    /// <summary>永久释放集合 即使清理失败也拒绝继续持有新对象</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Clear();
    }
}
