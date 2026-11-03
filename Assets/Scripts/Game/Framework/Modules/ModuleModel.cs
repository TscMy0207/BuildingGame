using System;

/// <summary>
/// 由自动注册管理的业务数据基类
/// 实例由所属容器创建和释放
/// </summary>
public abstract class ModuleModel : IModel, IDisposable
{
    /// <summary>
    /// 当前数据对象是否已经释放
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// 释放数据对象持有的资源
    /// 重复调用不会再次执行清理
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        OnDispose();
    }

    /// <summary>
    /// 由子类按需解除监听或释放自身持有的资源
    /// </summary>
    protected virtual void OnDispose() { }
}
