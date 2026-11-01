using System;
using UnityEngine;

/// <summary>
/// UI全局根节点 负责暴露各个UI层级的挂载点
/// </summary>
internal sealed class UIRoot : MonoBehaviour
{
    [SerializeField] private Transform m_pageRoot;
    [SerializeField] private Transform m_popupRoot;
    [SerializeField] private Transform m_overlayRoot;
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
