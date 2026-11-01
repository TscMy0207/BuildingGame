using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using VContainer;

/// <summary>
/// 模块Scope内可选使用的UI服务
/// </summary>
public interface IScopeUIService
{
    /// <summary>
    /// 使用当前模块Scope打开UI面板
    /// </summary>
    UniTask<UIPanelHandle> OpenAsync<TArgs>(UIKey<TArgs> key, TArgs args);

    /// <summary>
    /// 关闭当前模块Scope打开的UI面板
    /// </summary>
    UniTask CloseAsync(UIPanelHandle handle);

    /// <summary>
    /// 关闭当前模块Scope打开的全部UI面板
    /// </summary>
    UniTask CloseAllAsync();
}

/// <summary>
/// 模块Scope内UI服务实现
/// </summary>
internal sealed class ScopeUIService : IScopeUIService, IDisposable
{
    private readonly IUIService m_uiService;
    private readonly IObjectResolver m_scopeResolver;
    private readonly IScopeModule m_scopeModule;
    private readonly List<UIPanelHandle> m_openedPanels = new();

    public ScopeUIService(IUIService uiService, IObjectResolver scopeResolver, IScopeModule scopeModule)
    {
        m_uiService = uiService;
        m_scopeResolver = scopeResolver;
        m_scopeModule = scopeModule;
    }

    public async UniTask<UIPanelHandle> OpenAsync<TArgs>(UIKey<TArgs> key, TArgs args)
    {
        var lifetimeToken = m_scopeModule.LifetimeToken;
        lifetimeToken.ThrowIfCancellationRequested();

        var handle = await m_uiService.OpenAsync(key, args, m_scopeResolver, lifetimeToken);
        m_openedPanels.Add(handle);

        try
        {
            lifetimeToken.ThrowIfCancellationRequested();
            return handle;
        }
        catch
        {
            await CloseAsync(handle);
            throw;
        }
    }

    public async UniTask CloseAsync(UIPanelHandle handle)
    {
        if (!m_openedPanels.Remove(handle)) return;

        if (m_scopeModule.LifetimeToken.IsCancellationRequested)
        {
            m_uiService.DisposeImmediately(handle);
            return;
        }

        await m_uiService.CloseAsync(handle);
    }

    public async UniTask CloseAllAsync()
    {
        if (m_scopeModule.LifetimeToken.IsCancellationRequested)
        {
            Dispose();
            return;
        }

        var handles = new List<UIPanelHandle>(m_openedPanels);
        for (var i = handles.Count - 1; i >= 0; i--)
            await CloseAsync(handles[i]);
    }

    public void Dispose()
    {
        var handles = new List<UIPanelHandle>(m_openedPanels);
        m_openedPanels.Clear();

        for (var i = handles.Count - 1; i >= 0; i--)
            m_uiService.DisposeImmediately(handles[i]);
    }
}
