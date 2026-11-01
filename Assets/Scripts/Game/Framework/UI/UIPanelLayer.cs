/// <summary>
/// UI面板挂载层级
/// </summary>
public enum UIPanelLayer
{
    /// <summary>
    /// 页面层 常驻页面
    /// </summary>
    Page,

    /// <summary>
    /// 弹窗层 普通弹窗
    /// </summary>
    Popup,

    /// <summary>
    /// 覆盖层 高层级弹窗
    /// </summary>
    Overlay,

    /// <summary>
    /// 提示层 轻量提示
    /// </summary>
    Toast,
}
