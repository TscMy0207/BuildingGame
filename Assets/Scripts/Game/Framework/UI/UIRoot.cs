using System;
using UnityEngine;

/// <summary>
/// UI全局根节点 负责暴露各个UI层级的挂载点
/// </summary>
internal class UIRoot : MonoBehaviour
{
    /// <summary>
    /// 承载页面层面板实例的预制体挂载点
    /// </summary>
    [SerializeField] private Transform m_pageRoot;
    /// <summary>
    /// 承载弹窗层面板实例的预制体挂载点
    /// </summary>
    [SerializeField] private Transform m_popupRoot;
    /// <summary>
    /// 承载覆盖层面板实例的预制体挂载点
    /// </summary>
    [SerializeField] private Transform m_overlayRoot;
    /// <summary>
    /// 承载提示层面板实例的预制体挂载点
    /// </summary>
    [SerializeField] private Transform m_toastRoot;

    /// <summary>
    /// 根据面板层级获取实例挂载点
    /// </summary>
    internal Transform GetLayerRoot(UIPanelLayer layer)
    {
        var root = layer switch
        {
            UIPanelLayer.Page => m_pageRoot,
            UIPanelLayer.Popup => m_popupRoot,
            UIPanelLayer.Overlay => m_overlayRoot,
            UIPanelLayer.Toast => m_toastRoot,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
        };

        return root == null ? throw new InvalidOperationException($"UIRoot层级未配置:{layer}") : root;
    }
}
