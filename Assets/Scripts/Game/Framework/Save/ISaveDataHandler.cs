using Newtonsoft.Json.Linq;

/// <summary>
/// 管理一个Easy Save存档数据段的处理器
/// 实现类归属拥有该数据的程序集
/// </summary>
public interface ISaveDataHandler
{
    /// <summary>
    /// 数据段的稳定标识 不得使用框架保留前缀
    /// </summary>
    string Key { get; }
    /// <summary>
    /// 当前数据段版本 从一开始递增
    /// </summary>
    int CurrentVersion { get; }
    /// <summary>
    /// 恢复运行时默认数据
    /// </summary>
    void Reset();
    /// <summary>
    /// 使用具体数据类型将快照写入自身Key对应的缓存 不直接写入磁盘
    /// </summary>
    /// <param name="file">待提交的文件缓存 只能写入自身数据段</param>
    void Save(ES3File file);
    /// <summary>
    /// 从Easy Save缓存读取数据并应用到运行时
    /// </summary>
    /// <param name="file">包含当前数据段的文件缓存 不应修改缓存</param>
    /// <param name="savedVersion">存档记录的数据段版本 实现方负责兼容旧版本</param>
    void Load(ES3File file, int savedVersion);
}

/// <summary>
/// 仅用于迁移接入Easy Save之前的JSON数据
/// </summary>
internal interface ILegacyJsonSaveDataHandler
{
    /// <summary>
    /// 载入经过版本检查的旧JSON数据段
    /// </summary>
    /// <param name="payload">旧数据段内容</param>
    void LoadLegacy(JObject payload);
}
