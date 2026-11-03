using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

/// <summary>
/// 验证UI全局注入和异步打开取消的视图
/// </summary>
public class LifetimePanelProbe : UIPanelViewBase<int>
{
    /// <summary>
    /// 注入的全局业务依赖
    /// </summary>
    public LifetimeServiceTests.Dependency Dependency { get; private set; }
    /// <summary>
    /// 启用前是否已完成注入
    /// </summary>
    public bool EnabledAfterInjection { get; private set; }
    /// <summary>
    /// 是否保持异步打开以测试销毁取消
    /// </summary>
    [field: SerializeField]
    public bool WaitForCancellation { get; set; }
    /// <summary>
    /// 注入实际解析到的依赖
    /// </summary>
    [Inject]
    public void Construct(LifetimeServiceTests.Dependency dependency) => Dependency = dependency;
    /// <summary>
    /// 记录Unity启用时是否可以访问依赖
    /// </summary>
    private void OnEnable() => EnabledAfterInjection = Dependency != null;
    /// <summary>
    /// 按测试设置立即完成或等待销毁取消
    /// </summary>
    public override async UniTask OnOpeningAsync(int args, CancellationToken cancellationToken)
    {
        if (WaitForCancellation) await UniTask.WaitUntilCanceled(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
