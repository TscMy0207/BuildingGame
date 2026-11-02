using System;

/// <summary>
/// 由CoreComposition自动注册的业务系统基类 实例由所属容器管理
/// </summary>
public abstract class ModuleSystem : IDisposable
{
    /// <summary>当前系统是否已经释放</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>释放系统持有的资源 重复调用不会再次执行清理</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        OnDispose();
    }

    /// <summary>由子类按需取消任务 解绑事件或释放自身持有的资源</summary>
    protected virtual void OnDispose() { }
}
