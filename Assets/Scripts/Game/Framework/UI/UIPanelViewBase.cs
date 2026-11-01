using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// UI面板接口
/// </summary>
public interface IUIPanelView
{
}

/// <summary>
/// 带打开参数的UI面板接口
/// </summary>
/// <typeparam name="TArgs">UI打开参数类型</typeparam>
public interface IUIPanelView<TArgs> : IUIPanelView
{
    /// <summary>
    /// 面板打开时调用
    /// </summary>
    UniTask OnOpeningAsync(TArgs args, CancellationToken cancellationToken);
}

/// <summary>
/// 带打开参数的UI面板基础脚本
/// </summary>
/// <typeparam name="TArgs">UI打开参数类型</typeparam>
public abstract class UIPanelViewBase<TArgs> : MonoBehaviour, IUIPanelView<TArgs>
{
    /// <summary>
    /// 面板打开时调用
    /// </summary>
    public virtual UniTask OnOpeningAsync(TArgs args, CancellationToken cancellationToken)
    {
        return UniTask.CompletedTask;
    }
}
