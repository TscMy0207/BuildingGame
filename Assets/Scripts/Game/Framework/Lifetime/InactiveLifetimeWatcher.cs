using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 只监视从未激活的对象 弥补Unity不发送OnDestroy的生命周期边界
/// </summary>
internal class InactiveLifetimeWatcher : MonoBehaviour
{
    /// <summary>
    /// 当前运行中的监视器
    /// </summary>
    private static InactiveLifetimeWatcher s_instance;
    /// <summary>
    /// 从未激活对象和它们持有的资源
    /// </summary>
    private readonly Dictionary<LifetimeOwner, DisposableBag> _pending = new();
    /// <summary>
    /// 复用遍历列表以允许清理期间新增或删除监视
    /// </summary>
    private readonly List<LifetimeOwner> _snapshot = new();

    /// <summary>
    /// 兼容关闭Domain Reload后的重复运行
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => s_instance = null;

    /// <summary>
    /// 登记需要补充销毁检查的对象
    /// </summary>
    /// <param name="owner">生命周期组件</param>
    /// <param name="bag">不依赖Unity对象存活的清理集合</param>
    internal static void Watch(LifetimeOwner owner, DisposableBag bag)
    {
        if (s_instance == null)
        {
            var host = new GameObject("未激活对象生命周期监视器");
            DontDestroyOnLoad(host);
            s_instance = host.AddComponent<InactiveLifetimeWatcher>();
        }
        s_instance._pending[owner] = bag;
    }

    /// <summary>
    /// 对象正常销毁时无需继续监视
    /// </summary>
    /// <param name="owner">已经开始销毁的组件</param>
    internal static void Unwatch(LifetimeOwner owner)
    {
        if (s_instance != null) s_instance._pending.Remove(owner);
    }

    /// <summary>
    /// 补充清理从未激活便销毁的对象 已激活对象立即退出监视
    /// </summary>
    private void LateUpdate()
    {
        _snapshot.Clear();
        _snapshot.AddRange(_pending.Keys);
        foreach (var owner in _snapshot)
        {
            if (!_pending.TryGetValue(owner, out var bag)) continue;
            if (owner == null)
            {
                _pending.Remove(owner);
                LifetimeOwner.Release(bag);
            }
            else if (owner.HasAwakened) _pending.Remove(owner);
        }
        _snapshot.Clear();
    }

    /// <summary>
    /// 停止运行时完成尚未收到销毁通知的清理
    /// </summary>
    private void OnDestroy()
    {
        if (ReferenceEquals(s_instance, this)) s_instance = null;
        var bags = new List<DisposableBag>(_pending.Values);
        _pending.Clear();
        foreach (var bag in bags) LifetimeOwner.Release(bag);
    }
}
