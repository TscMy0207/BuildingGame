/// <summary>
/// UI面板配置表
/// </summary>
public sealed class UIPanelConfig
{
    /// <summary>
    /// UI键值主键
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// UI预制体的YooAsset资源路径
    /// </summary>
    public string PrefabLocation { get; }

    /// <summary>
    /// 面板挂载层级
    /// </summary>
    public UIPanelLayer Layer { get; }

    /// <summary>
    /// 创建只读的UI面板配置视图
    /// </summary>
    /// <param name="id">稳定面板ID</param>
    /// <param name="prefabLocation">YooAsset资源地址</param>
    /// <param name="layer">运行时UI层级</param>
    public UIPanelConfig(int id, string prefabLocation, UIPanelLayer layer)
    {
        Id = id;
        PrefabLocation = prefabLocation;
        Layer = layer;
    }
}
