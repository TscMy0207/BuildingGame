using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Luban;
using UnityEngine;

/// <summary>
/// 配置子服务契约 由启动总服务统一加载与清理
/// </summary>
public interface IConfigTableSubService
{
    /// <summary>
    /// 用于启动日志的配置集合名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 加载完整配置集合 成功后才发布实例
    /// </summary>
    /// <param name="cancellationToken">本次加载的取消令牌</param>
    /// <returns>配置加载任务</returns>
    UniTask LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 清除配置引用 并使尚未完成的加载失效
    /// </summary>
    void Clear();
}

/// <summary>
/// 异步读取Luban清单与二进制 并在完整解析后发布配置实例
/// </summary>
/// <typeparam name="TTables">Luban生成的配置集合类型</typeparam>
public abstract class ConfigTableSubServiceBase<TTables> : IConfigTableSubService where TTables : class
{
    /// <summary>
    /// 加载配置清单与二进制文件的资源入口
    /// </summary>
    private readonly IResourcesAssetLoader _resources;
    /// <summary>
    /// 记录表加载结果和验证失败原因
    /// </summary>
    private readonly ILogger<TTables> _logger;
    /// <summary>
    /// 防止同一个子服务并发加载
    /// </summary>
    private bool _isLoading;
    /// <summary>
    /// 清理后使尚未完成的加载失效
    /// </summary>
    private int _loadGeneration;

    /// <summary>
    /// 初始化资源加载依赖与日志
    /// </summary>
    /// <param name="name">配置集合名称</param>
    /// <param name="resources">资源加载服务</param>
    /// <param name="logger">配置日志</param>
    protected ConfigTableSubServiceBase(string name, IResourcesAssetLoader resources, ILogger<TTables> logger)
    {
        Name = name;
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 配置集合名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 包名与配置目录组成的资源前缀 不包含末尾斜线
    /// </summary>
    protected abstract string ResourcePrefix { get; }

    /// <summary>
    /// 与生成代码绑定的结构哈希
    /// </summary>
    protected abstract string ExpectedSchemaHash { get; }

    /// <summary>
    /// 最近一次成功加载的完整配置实例
    /// </summary>
    protected TTables Tables { get; private set; }

    /// <summary>
    /// 使用预加载的二进制构造配置并解析跨表引用
    /// </summary>
    /// <param name="loader">按Luban文件名获取二进制的同步回调</param>
    /// <returns>完整配置实例</returns>
    protected abstract TTables CreateTables(Func<string, ByteBuf> loader);

    /// <summary>
    /// 检查项目特有的配置约束
    /// </summary>
    /// <param name="tables">待发布的配置实例</param>
    protected virtual void ValidateTables(TTables tables) { }

    /// <summary>
    /// 读取并校验完整配置 失败时保留上一次有效实例
    /// </summary>
    /// <param name="cancellationToken">本次加载的取消令牌</param>
    /// <returns>配置加载任务</returns>
    public async UniTask LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isLoading) throw new InvalidOperationException($"{Name}配置正在加载 不允许并发加载");
        _isLoading = true;
        var generation = _loadGeneration;
        try
        {
            var manifestBytes = await ReadBytesAsync($"{ResourcePrefix}/manifest.json", cancellationToken);
            var manifest = JsonUtility.FromJson<ConfigTableManifest>(System.Text.Encoding.UTF8.GetString(manifestBytes));
            if (manifest == null || manifest.FormatVersion != 1 || manifest.SchemaHash != ExpectedSchemaHash ||
                manifest.Files == null || manifest.Files.Length == 0)
                throw new InvalidOperationException($"{Name}配置清单无效或与生成代码版本不匹配");

            var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                EnsureLoadIsCurrent(generation, cancellationToken);
                if (file == null || !IsValidFileName(file.Name) || string.IsNullOrEmpty(file.Sha256) || data.ContainsKey(file.Name))
                    throw new InvalidOperationException($"{Name}配置清单包含无效或重复文件");
                var bytes = await ReadBytesAsync($"{ResourcePrefix}/{file.Name}.bytes", cancellationToken);
                using (var sha = SHA256.Create())
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                        throw new InvalidOperationException($"{Name}配置文件校验失败:{file.Name}");
                }
                data.Add(file.Name, bytes);
            }

            EnsureLoadIsCurrent(generation, cancellationToken);
            var tables = CreateTables(fileName => data.TryGetValue(fileName, out var bytes)
                ? new ByteBuf(bytes)
                : throw new InvalidOperationException($"{Name}配置清单缺少文件:{fileName}"));
            ValidateTables(tables);
            EnsureLoadIsCurrent(generation, cancellationToken);
            Tables = tables ?? throw new InvalidOperationException($"{Name}配置解析结果为空");
            _logger.Log($"{Name}配置加载完成 共{data.Count}个文件");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.Error($"{Name}配置加载失败", exception);
            throw;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 等待资源加载结束后配对释放 避免仅取消等待而泄漏资源引用
    /// </summary>
    /// <param name="location">完整资源地址</param>
    /// <param name="cancellationToken">调用取消令牌</param>
    /// <returns>独立于TextAsset生命周期的二进制数据</returns>
    private async UniTask<byte[]> ReadBytesAsync(string location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TextAsset asset = null;
        try
        {
            asset = await _resources.LoadAssetAsync<TextAsset>(location);
            cancellationToken.ThrowIfCancellationRequested();
            if (asset == null) throw new InvalidOperationException($"配置资源为空:{location}");
            return asset.bytes;
        }
        finally
        {
            if (asset != null) _resources.ReleaseAsset<TextAsset>(location);
        }
    }

    /// <summary>
    /// 检查取消状态与清理代次
    /// </summary>
    /// <param name="generation">本次加载开始时的代次</param>
    /// <param name="cancellationToken">调用取消令牌</param>
    private void EnsureLoadIsCurrent(int generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _loadGeneration) throw new OperationCanceledException($"{Name}配置已清理 本次加载失效");
    }

    /// <summary>
    /// 限制生成文件名为单层文件 避免清单越过配置目录
    /// </summary>
    /// <param name="name">不含扩展名的生成文件名</param>
    /// <returns>是否为合法文件名</returns>
    private static bool IsValidFileName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains("..")) return false;
        foreach (var character in name)
            if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                !(character >= '0' && character <= '9') && character != '_' && character != '.' && character != '-') return false;
        return true;
    }

    /// <summary>
    /// 释放配置引用并禁止尚未结束的加载发布结果
    /// </summary>
    public void Clear()
    {
        _loadGeneration++;
        Tables = null;
    }
}

/// <summary>
/// 导出脚本生成的配置集合清单
/// </summary>
[Serializable]
internal class ConfigTableManifest
{
    /// <summary>
    /// 清单格式版本
    /// </summary>
    public int FormatVersion;
    /// <summary>
    /// 生成代码的结构哈希
    /// </summary>
    public string SchemaHash;
    /// <summary>
    /// 完整二进制文件列表
    /// </summary>
    public ConfigTableManifestFile[] Files;
}

/// <summary>
/// 清单中的单个二进制文件及其内容校验值
/// </summary>
[Serializable]
internal class ConfigTableManifestFile
{
    /// <summary>
    /// 不含扩展名的Luban文件名
    /// </summary>
    public string Name;
    /// <summary>
    /// 文件内容的SHA256校验值
    /// </summary>
    public string Sha256;
}
