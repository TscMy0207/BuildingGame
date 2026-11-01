using VContainer;
using VContainer.Unity;
using MessagePipe;

/// <summary>
/// Framework组合根注册入口
/// </summary>
public static class FrameworkComposition
{
    /// <summary>
    /// 注册Framework默认服务和生命周期
    /// </summary>
    public static void RegisterDefaults(IContainerBuilder builder)
    {
        var messagePipeOptions = builder.RegisterMessagePipe(options =>
        {
            options.InstanceLifetime = InstanceLifetime.Singleton;
        });
        builder.RegisterMessageBroker<int, object>(messagePipeOptions);

        // 日志服务
        builder.Register<LoggingService>(Lifetime.Singleton).As<ILoggingService>().As<IStartupService>();
        builder.Register(typeof(Logger<>), Lifetime.Singleton).As(typeof(ILogger<>));

        // 时间服务
        builder.Register<TimerService>(Lifetime.Singleton).As<ITimerService>().As<IStartupService>();
        // 事件服务
        builder.Register<EventService>(Lifetime.Singleton).As<IEventService>().As<IStartupService>();
        // 资源服务
        builder.Register<ResourcesPackageOptionsFactory>(Lifetime.Singleton);
        builder.Register<ResourcesPackageManager>(Lifetime.Singleton).As<IResourcesPackageManager>();
        builder.Register<ResourcesService>(Lifetime.Singleton).As<IResourcesService>().As<IResourcesAssetLoader>().As<IStartupService>();
        // 对象池服务
        builder.Register<ObjectPoolService>(Lifetime.Singleton).As<IObjectPoolService>().As<IStartupService>();
        // 配置表服务 总服务和Framework子服务
        builder.Register<FrameworkConfigSubService>(Lifetime.Singleton).As<IFrameworkConfigTableService>().As<IConfigTableSubService>();
        builder.Register<ConfigTableService>(Lifetime.Singleton).As<IStartupService>();
        // UI服务
        builder.Register<UIService>(Lifetime.Singleton).As<IUIService>().As<IStartupService>();
        // 摄像机服务
        builder.Register<CameraService>(Lifetime.Singleton).As<ICameraService>().As<IStartupService>();
        // 模块服务
        builder.Register<ModuleCatalog>(Lifetime.Singleton).AsSelf().As<IModuleCatalog>();
        builder.Register<ModuleService>(Lifetime.Singleton).As<IModuleService>().As<IStartupService>();
        // 保存服务
        builder.Register<SaveService>(Lifetime.Singleton).As<ISaveService>().As<IStartupService>();
        // 音频设置以及音频服务
        builder.Register<AudioSettingsSaveDataHandler>(Lifetime.Singleton).As<ISaveDataHandler>().As<IAudioSettings>();
        builder.Register<AudioService>(Lifetime.Singleton).As<IAudioService>().As<IStartupService>();
        // 启动器服务
        builder.Register<ServiceStartupCoordinator>(Lifetime.Singleton).As<IServiceStartupCoordinator>();
    }
}
