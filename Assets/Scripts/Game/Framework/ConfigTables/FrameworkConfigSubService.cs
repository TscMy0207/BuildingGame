using System;
using BuildingGame.Config.Framework;
using Luban;

/// <summary>
/// 框架配置访问契约
/// </summary>
public interface IFrameworkConfigTableService
{
    /// <summary>
    /// 已加载的Luban框架配置 未初始化时抛出明确异常
    /// </summary>
    FrameworkTables FrameworkTables { get; }

    /// <summary>
    /// 查询面板配置并转换为UI服务使用的层级类型
    /// </summary>
    /// <param name="id">稳定面板ID</param>
    /// <param name="config">找到的面板配置</param>
    /// <returns>是否找到配置</returns>
    bool TryGetPanelConfig(int id, out UIPanelConfig config);
}

/// <summary>
/// 加载Luban框架配置并为UI系统提供类型明确的访问接口
/// </summary>
internal sealed class FrameworkConfigSubService : ConfigTableSubServiceBase<FrameworkTables>, IFrameworkConfigTableService
{
    /// <summary>
    /// 创建框架配置子服务
    /// </summary>
    /// <param name="resources">资源加载服务</param>
    /// <param name="logger">框架配置日志</param>
    public FrameworkConfigSubService(IResourcesAssetLoader resources, ILogger<FrameworkTables> logger)
        : base("Framework", resources, logger) { }

    /// <summary>
    /// 框架配置在YooAsset中的资源前缀
    /// </summary>
    protected override string ResourcePrefix => FrameworkSettings.FrameworkConfigLocation;

    /// <summary>
    /// 当前生成代码对应的结构哈希
    /// </summary>
    protected override string ExpectedSchemaHash => BuildingGame.Config.Framework.FrameworkTables.SchemaHash;

    /// <summary>
    /// 已成功加载的框架配置
    /// </summary>
    public FrameworkTables FrameworkTables => Tables ?? throw new InvalidOperationException("框架配置尚未加载或已清理");

    /// <summary>
    /// 创建配置集合并由生成代码解析表间引用
    /// </summary>
    /// <param name="loader">二进制读取回调</param>
    /// <returns>完整配置集合</returns>
    protected override FrameworkTables CreateTables(Func<string, ByteBuf> loader) => new(loader);

    /// <summary>
    /// 检查面板地址与层级映射
    /// </summary>
    /// <param name="tables">待发布的框架配置</param>
    protected override void ValidateTables(FrameworkTables tables)
    {
        foreach (var record in tables.TbUIPanel.DataList)
        {
            if (record.Id <= 0 || string.IsNullOrWhiteSpace(record.PrefabLocation) || record.PrefabLocation.IndexOf('/') <= 0)
                throw new InvalidOperationException($"UI面板配置的ID或资源地址无效:{record.Id}");
            ToPanelLayer(record.Layer);
        }
    }

    /// <summary>
    /// 按稳定ID查询面板配置
    /// </summary>
    /// <param name="id">面板ID</param>
    /// <param name="config">UI服务使用的配置</param>
    /// <returns>是否找到面板</returns>
    public bool TryGetPanelConfig(int id, out UIPanelConfig config)
    {
        var record = FrameworkTables.TbUIPanel.GetOrDefault(id);
        config = record == null ? null : new UIPanelConfig(record.Id, record.PrefabLocation, ToPanelLayer(record.Layer));
        return config != null;
    }

    /// <summary>
    /// 显式映射配置枚举 避免依赖两个枚举的底层数值相同
    /// </summary>
    /// <param name="layer">Luban配置层级</param>
    /// <returns>运行时UI层级</returns>
    private static UIPanelLayer ToPanelLayer(PanelLayer layer) => layer switch
    {
        PanelLayer.Page => UIPanelLayer.Page,
        PanelLayer.Popup => UIPanelLayer.Popup,
        PanelLayer.Overlay => UIPanelLayer.Overlay,
        PanelLayer.Toast => UIPanelLayer.Toast,
        _ => throw new InvalidOperationException($"UI面板配置包含未知层级:{layer}")
    };
}
