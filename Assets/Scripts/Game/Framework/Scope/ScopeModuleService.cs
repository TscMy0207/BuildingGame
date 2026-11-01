using Cysharp.Threading.Tasks;

/// <summary>
/// 当前模块实例使用的模块操作服务
/// </summary>
public interface IScopeModuleService
{
    /// <summary>
    /// 打开一个独立模块
    /// </summary>
    /// <param name="moduleKey">模块键</param>
    /// <param name="args">模块参数</param>
    /// <typeparam name="TArgs">参数类型</typeparam>
    /// <returns>模块句柄</returns>
    UniTask<ModuleHandle> OpenAsync<TArgs>(ModuleKey<TArgs> moduleKey, TArgs args);

    /// <summary>
    /// 关闭当前模块
    /// </summary>
    UniTask CloseAsync();
}

/// <summary>
/// 模块Scope内的接口实现
/// </summary>
internal sealed class ScopeModuleService : IScopeModuleService
{
    private readonly IModuleService m_moduleService;
    private readonly IScopeModule m_scopeModule;
    private readonly IModuleContext m_context;

    public ScopeModuleService(IModuleService moduleService, IScopeModule scopeModule, IModuleContext context)
    {
        m_moduleService = moduleService;
        m_scopeModule = scopeModule;
        m_context = context;
    }

    /// <summary>
    /// 打开一个独立模块
    /// </summary>
    public UniTask<ModuleHandle> OpenAsync<TArgs>(ModuleKey<TArgs> moduleKey, TArgs args)
    {
        m_scopeModule.LifetimeToken.ThrowIfCancellationRequested();
        return m_moduleService.OpenAsync(moduleKey, args, m_scopeModule.LifetimeToken);
    }

    /// <summary>
    /// 关闭当前模块
    /// </summary>
    public async UniTask CloseAsync()
    {
        m_scopeModule.LifetimeToken.ThrowIfCancellationRequested();
        await m_moduleService.CloseAsync(m_context.ModuleHandle);
    }
}
