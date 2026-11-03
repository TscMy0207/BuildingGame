using System;
using MessagePipe;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Framework特殊注册入口
/// 只保留无法通过自动注册表达的服务
/// </summary>
public static class FrameworkComposition
{
    /// <summary>
    /// 注册消息管道和开放泛型日志器
    /// </summary>
    /// <param name="builder">目标容器</param>
    public static void RegisterSpecial(IContainerBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        var messagePipeOptions = builder.RegisterMessagePipe(options =>
        {
            options.InstanceLifetime = InstanceLifetime.Singleton;
        });
        builder.RegisterMessageBroker<int, object>(messagePipeOptions);
        builder.Register(typeof(Logger<>), Lifetime.Singleton).As(typeof(ILogger<>));
    }
}
