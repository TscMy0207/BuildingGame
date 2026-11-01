using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;

/// <summary>
/// 模块操作服务
/// </summary>
public interface IModuleService
{
    /// <summary>
    /// 打开一个独立模块
    /// </summary>
    /// <param name="moduleKey">模块键</param>
    /// <param name="args">模块参数</param>
    /// <param name="cancellationToken">仅取消本次打开请求 模块成功打开后不会成为模块生命周期令牌</param>
    /// <typeparam name="TArgs">模块参数类型</typeparam>
    /// <returns>模块句柄</returns>
    UniTask<ModuleHandle> OpenAsync<TArgs>(ModuleKey<TArgs> moduleKey, TArgs args, CancellationToken cancellationToken);
    
    /// <summary>
    /// 关闭一个模块实例
    /// </summary>
    /// <param name="handle">模块句柄</param>
    /// <returns>关闭任务</returns>
    UniTask CloseAsync(ModuleHandle handle);
}

/// <summary>
/// 模块服务
/// </summary>
internal sealed class ModuleService : IStartupService, IModuleService
{
    #region 服务生命周期

    public int Priority => FrameworkSettings.ModuleStartupPriority;
    public string Description => "模块服务";
    public float Weight => FrameworkSettings.ModuleStartupWeight;

    public UniTask StartAsync(StartupContext context)
    {
        m_lifetimeToken = context.LifetimeToken;
        return UniTask.CompletedTask;
    }

    public void Dispose()
    {
        // 释放所有模块实例
        var modules = new List<(ModuleHandle Handle, ScopeModule Module)>(m_modules.Values);
        m_modules.Clear();

        foreach (var module in modules)
            DestroyImmediately(module.Module);
    }

    #endregion

    private readonly IObjectResolver m_rootResolver; // 根解析器 用于解析模块依赖项
    private readonly ILogger<ModuleService> m_logger;
    private readonly ModuleCatalog m_catalog;
    private readonly Dictionary<int, (ModuleHandle Handle, ScopeModule Module)> m_modules = new();

    private int m_nextHandleId; // 下一个模块句柄

    private CancellationToken m_lifetimeToken;

    public ModuleService(IObjectResolver rootResolver, ILogger<ModuleService> logger, ModuleCatalog catalog)
    {
        m_rootResolver = rootResolver;
        m_logger = logger;
        m_catalog = catalog;
    }

    public async UniTask<ModuleHandle> OpenAsync<TArgs>(ModuleKey<TArgs> moduleKey, TArgs args, CancellationToken cancellationToken)
    {
        m_lifetimeToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();

        if (!m_catalog.TryGetRegistration(moduleKey.Id, out var registration)) throw new KeyNotFoundException($"模块未注册{moduleKey.Name}");
        if (m_modules.ContainsKey(moduleKey.Id)) throw new InvalidOperationException($"模块{moduleKey.Name}已打开");

        var handle = new ModuleHandle(moduleKey.Id, ++m_nextHandleId);
        ScopeModule scopeModule = null;
        
        try
        {
            scopeModule = registration.CreateModule();
            m_modules.Add(moduleKey.Id, (handle, scopeModule));
            
            // 构建ModuleContext
            var context = new ModuleContext<TArgs>(handle, args);
            scopeModule.Context = context;
            // 构建Scope
            scopeModule.Scope = m_rootResolver.CreateScope(builder =>
            {
                builder.RegisterInstance<IModuleContext>(context);
                builder.RegisterInstance<IScopeModule>(scopeModule);
                scopeModule.Configure(builder);
            });

            // 启动ScopeModule
            await scopeModule.StartAsync();
            
            cancellationToken.ThrowIfCancellationRequested();
            m_lifetimeToken.ThrowIfCancellationRequested();
            return handle;
        }
        catch (Exception e)
        {
            if (scopeModule is not null)
            {
                try
                {
                    await scopeModule.StopAsync();
                }
                catch (Exception cleanupException)
                {
                    m_logger.Error($"打开模块{moduleKey.Name}时出现异常，尝试关闭模块{handle}时出现异常", cleanupException);
                }
                finally
                {
                    // 关闭模块后 移除模块实例
                    if (m_modules.TryGetValue(handle.ModuleId, out var registeredModule) && registeredModule.Handle.Equals(handle))
                        m_modules.Remove(handle.ModuleId);
                }
            }

            if (e is not OperationCanceledException)
                m_logger.Error($"打开模块{moduleKey.Name}时出现异常", e);
            throw;
        }
    }

    /// <summary>
    /// 关闭模块
    /// </summary>
    public async UniTask CloseAsync(ModuleHandle handle)
    {
        if (!m_modules.TryGetValue(handle.ModuleId, out var module) || !module.Handle.Equals(handle)) return;

        var scopeModule = module.Module;
        if (scopeModule.LifetimeToken.IsCancellationRequested) return;

        try
        {
            await scopeModule.StopAsync();
        }
        catch (Exception e)
        {
            m_logger.Error($"关闭模块{handle}时出现异常", e);
            throw;
        }
        finally
        {
            if (m_modules.TryGetValue(handle.ModuleId, out var registeredModule) && registeredModule.Handle.Equals(handle))
                m_modules.Remove(handle.ModuleId);
        }
    }
    
    /// <summary>
    /// 强制释放模块实例
    /// </summary>
    private void DestroyImmediately(ScopeModule scopeModule)
    {
        try
        {
            scopeModule.Dispose();
        }
        catch (Exception e)
        {
            m_logger.Error("强制释放模块时出现异常", e);
        }
    }
}
