using System;
using System.Threading;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>独占一次预制体加载引用 在对应实例销毁后归还</summary>
internal sealed class ResourceInstanceLifetime : LifetimeHandle
{
    /// <summary>本次引用所属的加载器</summary>
    private IResourcesAssetLoader _resources;
    /// <summary>加载时使用的原始地址</summary>
    private readonly string _location;

    /// <summary>接管已经加载成功的一次引用</summary>
    private ResourceInstanceLifetime(IResourcesAssetLoader resources, string location)
    {
        _resources = resources;
        _location = location;
    }

    /// <summary>在未激活节点下创建并绑定引用 失败时同时回滚实例和引用</summary>
    /// <param name="resources">引用所属服务</param>
    /// <param name="location">已加载的资源地址</param>
    /// <param name="prefab">加载结果</param>
    /// <param name="resolver">可选依赖解析器</param>
    /// <param name="parent">目标父节点</param>
    /// <param name="worldPositionStays">是否保留预制体世界坐标</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建完成的实例</returns>
    internal static GameObject Create(IResourcesAssetLoader resources, string location, GameObject prefab,
        IObjectResolver resolver = null, Transform parent = null, bool worldPositionStays = false,
        CancellationToken cancellationToken = default)
    {
        var reference = new ResourceInstanceLifetime(resources, location);
        GameObject staging = null;
        GameObject instance = null;
        var transferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prefab == null) throw new InvalidOperationException($"预制体资源为空:{location}");
            // 未激活父节点阻止Awake先于依赖注入执行 不修改原始预制体状态
            staging = new GameObject("资源实例初始化节点");
            staging.SetActive(false);
            staging.transform.SetParent(parent, false);
            instance = Object.Instantiate(prefab, staging.transform, worldPositionStays);
            var active = instance.activeSelf;
            instance.SetActive(false);
            reference.AddTo(instance);
            transferred = true;
            resolver?.InjectGameObject(instance);
            cancellationToken.ThrowIfCancellationRequested();
            if (instance == null) throw new InvalidOperationException($"预制体在注入期间被销毁:{location}");
            instance.transform.SetParent(parent, false);
            instance.SetActive(active);
            cancellationToken.ThrowIfCancellationRequested();
            if (instance == null) throw new InvalidOperationException($"预制体在初始化期间被销毁:{location}");
            return instance;
        }
        catch
        {
            if (instance != null) Object.Destroy(instance);
            if (!transferred) reference.Dispose();
            throw;
        }
        finally
        {
            if (staging != null) Object.Destroy(staging);
        }
    }

    /// <summary>只归还当前实例取得的引用 其他实例和对象池各自保有所有权</summary>
    protected override void OnDispose()
    {
        var resources = _resources;
        _resources = null;
        resources?.ReleaseAsset<GameObject>(_location);
    }
}
