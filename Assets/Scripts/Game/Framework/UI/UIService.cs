using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// UI服务接口
/// </summary>
public interface IUIService
{
    /// <summary>
    /// 打开指定UI面板
    /// 加载并实例化预制体后注册实例
    /// 返回的UIPanelHandle由调用方或所在Scope负责关闭
    /// 打开失败时会清理已创建的游戏对象和资源引用
    /// </summary>
    /// <param name="key">UI键值</param>
    /// <param name="args">UI打开参数</param>
    /// <param name="scopeResolver">对象解析器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>UI面板句柄</returns>
    UniTask<UIPanelHandle> OpenAsync<TArgs>(UIKey<TArgs> key, TArgs args, IObjectResolver scopeResolver, CancellationToken cancellationToken);

    /// <summary>
    /// 关闭指定UI面板
    /// </summary>
    /// <param name="handle">UI面板句柄</param>
    /// <returns>关闭任务</returns>
    UniTask CloseAsync(UIPanelHandle handle);

    /// <summary>
    /// 立即释放指定UI面板
    /// 销毁面板游戏对象并释放对应资源引用
    /// </summary>
    /// <param name="handle">UI面板句柄</param>
    void DisposeImmediately(UIPanelHandle handle);
}

/// <summary>
/// 全局UI基础服务
/// </summary>
internal sealed class UIService : IStartupService, IUIService
{
    #region 服务生命周期

    public int Priority => FrameworkSettings.UIStartupPriority;
    public string Description => "UI服务";
    public float Weight => FrameworkSettings.UIStartupWeight;

    public async UniTask StartAsync(StartupContext context)
    {
        m_lifetimeToken = context.LifetimeToken;
        context.LifetimeToken.ThrowIfCancellationRequested();
        context.ReportProgress?.Invoke(0.1f, "加载UIRoot");
        await InitializeRootAsync();
        context.LifetimeToken.ThrowIfCancellationRequested();
        context.ReportProgress?.Invoke(1f, "UIRoot初始化完成");
    }

    public void Dispose()
    {
        if (m_uiRoot == null && m_instances.Count == 0)
            return;
        
        var handles = new List<UIPanelHandle>(m_instances.Keys);
        for (var i = handles.Count - 1; i >= 0; i--)
            DisposeImmediately(handles[i]);

        if (m_uiRoot == null)
            return;

        Object.Destroy(m_uiRoot.gameObject);
        m_resources.ReleaseAsset<GameObject>(RootPrefab);
        m_uiRoot = null;
    }

    #endregion

    private readonly IResourcesService m_resources;
    private readonly IFrameworkConfigTableService m_configTableService;
    private readonly ILogger<UIService> m_logger;
    private readonly Dictionary<UIPanelHandle, PanelInstance> m_instances = new();

    private UIRoot m_uiRoot;
    private CancellationToken m_lifetimeToken = new(true);
    private int m_nextHandleId;
    
    private const string RootPrefab = FrameworkSettings.UIRootLocation;

    public UIService(IResourcesService resources, IFrameworkConfigTableService configTableService, ILogger<UIService> logger)
    {
        m_resources = resources;
        m_configTableService = configTableService;
        m_logger = logger;
    }

    /// <summary>
    /// 初始化UI根节点
    /// </summary>
    private async UniTask InitializeRootAsync()
    {
        if (m_uiRoot != null)
            return;

        GameObject instance = null;
        try
        {
            instance = await m_resources.LoadAndInstantiatePrefabAsync(RootPrefab);
            m_uiRoot = instance.GetComponent<UIRoot>();
            if (m_uiRoot == null)
                throw new InvalidOperationException($"UIRoot prefab缺少UIRoot脚本:{RootPrefab}");

            Object.DontDestroyOnLoad(instance);
        }
        catch
        {
            if (instance != null)
            {
                Object.Destroy(instance);
                m_resources.ReleaseAsset<GameObject>(RootPrefab);
            }

            m_uiRoot = null;
            throw;
        }
    }

    /// <summary>
    /// 打开UI面板
    /// </summary>
    public async UniTask<UIPanelHandle> OpenAsync<TArgs>(UIKey<TArgs> key, TArgs args, IObjectResolver scopeResolver, CancellationToken cancellationToken)
    {
        if (scopeResolver == null) throw new ArgumentNullException(nameof(scopeResolver));

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetimeToken, cancellationToken);
        var openingToken = linkedCancellation.Token;
        openingToken.ThrowIfCancellationRequested();

        if (!m_configTableService.TryGetPanelConfig(key.Id, out var config)) throw new KeyNotFoundException($"未找到UI面板配置{key.Name}");

        GameObject instanceObject = null;
        PanelInstance panelInstance = null;

        try
        {
            // 加载预制体
            var parent = m_uiRoot.GetLayerRoot(config.Layer);
            instanceObject = await m_resources.LoadAndInstantiatePrefabWithResolverAsync(config.PrefabLocation, scopeResolver, parent, false, openingToken);
            openingToken.ThrowIfCancellationRequested();

            var panelView = instanceObject.GetComponent<IUIPanelView>();
            if (panelView is not IUIPanelView<TArgs> typedPanelView)
            {
                var actualType = panelView == null ? "缺少IUIPanelView" : panelView.GetType().Name;
                throw new InvalidOperationException($"UI面板{key.Name}参数类型不匹配 期望{typeof(TArgs).Name}实际{actualType}");
            }

            // 注册实例
            var handle = new UIPanelHandle(++m_nextHandleId);
            panelInstance = new PanelInstance(handle, key.Id, instanceObject, config.PrefabLocation);
            m_instances.Add(handle, panelInstance);

            await typedPanelView.OnOpeningAsync(args, openingToken);
            openingToken.ThrowIfCancellationRequested();
            return handle;
        }
        catch (Exception e)
        {
            try
            {
                if (panelInstance != null) DisposeImmediately(panelInstance.Handle);
                else
                {
                    if (instanceObject != null)
                    {
                        Object.Destroy(instanceObject);
                        m_resources.ReleaseAsset<GameObject>(config.PrefabLocation);
                    }
                }
            }
            catch (Exception cleanupException)
            {
                m_logger.Error($"打开UI面板{key.Name}时清理失败", cleanupException);
            }

            if (e is not OperationCanceledException)
                m_logger.Error($"打开UI面板{key.Name}时出现异常", e);
            throw;
        }
    }

    public UniTask CloseAsync(UIPanelHandle handle)
    {
        DisposeImmediately(handle);
        return UniTask.CompletedTask;
    }

    public void DisposeImmediately(UIPanelHandle handle)
    {
        if (!m_instances.TryGetValue(handle, out var instance))
            return;

        try
        {
            DisposeUIPanel(instance);
        }
        catch (Exception e)
        {
            m_logger.Error($"立即释放UI面板{instance.Id}时出现异常", e);
        }
    }

    /// <summary>
    /// 释放UI面板实例
    /// </summary>
    private void DisposeUIPanel(PanelInstance instance)
    {
        // 从实例字典中移除
        if (!m_instances.Remove(instance.Handle))
            return;

        // 释放面板游戏对象
        Object.Destroy(instance.GameObject);
        m_resources.ReleaseAsset<GameObject>(instance.PrefabLocation);
    }

    private sealed class PanelInstance
    {
        public int Id { get; }
        public string PrefabLocation { get; }
        public GameObject GameObject { get; }
        public UIPanelHandle Handle { get; }

        public PanelInstance(UIPanelHandle handle, int id, GameObject gameObject, string prefabLocation)
        {
            Handle = handle;
            Id = id;
            GameObject = gameObject;
            PrefabLocation = prefabLocation;
        }
    }}
