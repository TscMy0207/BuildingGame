using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>将事件 定时器及其他可释放对象链式绑定到指定生命周期</summary>
public static class DisposableExtensions
{
    /// <summary>将对象加入可复用的释放集合</summary>
    /// <typeparam name="T">可释放对象类型</typeparam>
    /// <param name="disposable">待绑定对象</param>
    /// <param name="bag">生命周期集合</param>
    /// <returns>原始对象以便继续调用或保存</returns>
    public static T AddTo<T>(this T disposable, DisposableBag bag) where T : IDisposable
    {
        if (bag == null) throw new ArgumentNullException(nameof(bag));
        bag.Add(disposable);
        return disposable;
    }

    /// <summary>绑定到组件所在GameObject的销毁 不会在禁用或归还对象池时结束</summary>
    /// <typeparam name="T">可释放对象类型</typeparam>
    /// <param name="disposable">待绑定对象</param>
    /// <param name="owner">生命周期所属组件</param>
    /// <returns>原始对象</returns>
    public static T AddTo<T>(this T disposable, Component owner) where T : IDisposable =>
        disposable.AddTo(owner == null ? null : owner.gameObject);

    /// <summary>绑定到GameObject销毁 同时覆盖从未激活的对象</summary>
    /// <typeparam name="T">可释放对象类型</typeparam>
    /// <param name="disposable">待绑定对象</param>
    /// <param name="owner">生命周期所属对象</param>
    /// <returns>原始对象</returns>
    public static T AddTo<T>(this T disposable, GameObject owner) where T : IDisposable
    {
        if (disposable == null) throw new ArgumentNullException(nameof(disposable));
        if (owner == null) { disposable.Dispose(); return disposable; }
        if (disposable is LifetimeHandle handle && handle.IsDisposed) return disposable;
        var lifetime = owner.GetComponent<LifetimeOwner>();
        if (lifetime == null) lifetime = owner.AddComponent<LifetimeOwner>();
        lifetime.Add(disposable);
        return disposable;
    }

    /// <summary>令牌取消时结束对象 后台取消会调度回Unity主线程</summary>
    /// <typeparam name="T">可释放对象类型</typeparam>
    /// <param name="disposable">待绑定对象</param>
    /// <param name="cancellationToken">生命周期取消令牌</param>
    /// <returns>原始对象</returns>
    public static T AddTo<T>(this T disposable, CancellationToken cancellationToken) where T : IDisposable
    {
        if (disposable == null) throw new ArgumentNullException(nameof(disposable));
        if (!cancellationToken.CanBeCanceled) return disposable;
        if (disposable is LifetimeHandle ended && ended.IsDisposed) return disposable;
        var binding = new CancellationBinding(disposable);
        binding.Register(cancellationToken);
        return disposable;
    }

    /// <summary>跟踪句柄自然结束并及时移除令牌回调 避免长生命周期令牌积累已完成任务</summary>
    private sealed class CancellationBinding
    {
        /// <summary>当前仍绑定的对象</summary>
        private IDisposable _disposable;
        /// <summary>需要解除的令牌回调</summary>
        private CancellationTokenRegistration _registration;

        /// <summary>先订阅完成通知以覆盖令牌已取消的情况</summary>
        /// <param name="disposable">待绑定对象</param>
        internal CancellationBinding(IDisposable disposable)
        {
            _disposable = disposable;
            if (disposable is LifetimeHandle handle) handle.Completed += OnCompleted;
        }

        /// <summary>登记取消回调并处理同步完成的注册</summary>
        /// <param name="token">取消令牌</param>
        internal void Register(CancellationToken token)
        {
            _registration = token.Register(Cancel);
            if (_disposable == null) _registration.Dispose();
        }

        /// <summary>确保服务清理发生在Unity主线程</summary>
        private void Cancel()
        {
            if (PlayerLoopHelper.IsMainThread) Release();
            else UniTask.Post(Release);
        }

        /// <summary>取消后清除引用并防止重复释放普通IDisposable</summary>
        private void Release()
        {
            var disposable = _disposable;
            _disposable = null;
            if (disposable is LifetimeHandle handle) handle.Completed -= OnCompleted;
            _registration.Dispose();
            disposable?.Dispose();
        }

        /// <summary>解除令牌和句柄之间的双向引用</summary>
        /// <param name="handle">已经结束的句柄</param>
        private void OnCompleted(LifetimeHandle handle)
        {
            handle.Completed -= OnCompleted;
            _disposable = null;
            _registration.Dispose();
        }
    }
}
