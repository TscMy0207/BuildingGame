using System;
using UnityEngine;

/// <summary>在GameObject销毁时释放其订阅 任务及实例资源引用</summary>
[DisallowMultipleComponent]
public sealed class LifetimeOwner : MonoBehaviour
{
    /// <summary>绑定到当前对象的清理集合</summary>
    private readonly DisposableBag _disposables = new();
    /// <summary>标记是否收到过Awake 从未激活的对象需要销毁监视</summary>
    internal bool HasAwakened { get; private set; }

    /// <summary>激活后由Unity的OnDestroy负责清理</summary>
    private void Awake() => HasAwakened = true;

    /// <summary>登记清理对象并在必要时启用未激活对象监视</summary>
    /// <param name="disposable">需要随对象销毁而结束的引用</param>
    internal void Add(IDisposable disposable)
    {
        _disposables.Add(disposable);
        if (!HasAwakened) InactiveLifetimeWatcher.Watch(this, _disposables);
    }

    /// <summary>释放当前对象持有的引用</summary>
    private void OnDestroy()
    {
        InactiveLifetimeWatcher.Unwatch(this);
        Release(_disposables);
    }

    /// <summary>完成全部清理并报告异常 避免影响其他Unity销毁回调</summary>
    /// <param name="bag">待清理的集合</param>
    internal static void Release(DisposableBag bag)
    {
        try { bag.Dispose(); }
        catch (Exception exception) { Debug.LogError($"释放对象生命周期资源失败:{exception}"); }
    }
}
