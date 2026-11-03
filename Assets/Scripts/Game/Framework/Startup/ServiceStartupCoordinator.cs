using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZLinq;

/// <summary>
/// 基础服务启动协调器
/// 按优先级串行启动所有基础服务 并对外上报进度和失败
/// </summary>
public interface IServiceStartupCoordinator
{
    /// <summary>
    /// 启动进度变化 参数为总进度和当前描述
    /// </summary>
    event Action<float, string> ProgressChanged;

    /// <summary>
    /// 服务启动失败 参数为服务描述和失败原因
    /// </summary>
    event Action<string, string> Failed;

    /// <summary>
    /// 全部基础服务启动完成
    /// </summary>
    event Action Completed;

    /// <summary>
    /// 启动所有基础服务
    /// </summary>
    UniTask StartAsync();
}

/// <summary>
/// 基础服务启动上下文
/// </summary>
public sealed class StartupContext
{
    /// <summary>
    /// 基础服务生命周期取消令牌
    /// </summary>
    public CancellationToken LifetimeToken { get; }

    /// <summary>
    /// 上报服务内部进度 参数为0到1的进度和描述
    /// </summary>
    public Action<float, string> ReportProgress { get; }

    public StartupContext(CancellationToken lifetimeToken, Action<float, string> reportProgress)
    {
        LifetimeToken = lifetimeToken;
        ReportProgress = reportProgress;
    }
}

/// <summary>
/// 基础服务生命周期管理器
/// </summary>
[AutoRegister(typeof(IServiceStartupCoordinator), AsSelf = false)]
internal class ServiceStartupCoordinator : ISystem, IServiceStartupCoordinator, IDisposable
{
    private readonly IReadOnlyList<IStartupService> m_services;
    private readonly ILogger<ServiceStartupCoordinator> m_logger;
    private readonly CancellationTokenSource m_lifetimeCancellation = new();
    private readonly CancellationToken m_lifetimeToken;

    private const float WeightEpsilon = 0.001f;

    private float m_weightScale;

    public event Action<float, string> ProgressChanged;
    public event Action<string, string> Failed;
    public event Action Completed;

    public ServiceStartupCoordinator(IEnumerable<IStartupService> services, ILogger<ServiceStartupCoordinator> logger)
    {
        m_services = services.AsValueEnumerable().OrderBy(service => service.Priority).ToList();
        m_logger = logger;
        m_lifetimeToken = m_lifetimeCancellation.Token;
    }

    public async UniTask StartAsync()
    {
        if (m_lifetimeToken.IsCancellationRequested)
            throw new ObjectDisposedException(nameof(ServiceStartupCoordinator), "基础服务管理器已释放 无法启动基础服务");

        var totalWeight = m_services.AsValueEnumerable().Sum(service => service.Weight);
        m_weightScale = ComputeWeightScale(totalWeight);

        await StartServicesAsync(m_lifetimeToken);
    }

    /// <summary>
    /// 计算权重缩放因子
    /// 权重总和接近1时不需要缩放 否则按总和归一化
    /// </summary>
    private float ComputeWeightScale(float totalWeight)
    {
        if (Math.Abs(totalWeight - 1f) <= WeightEpsilon) return 1f;
        var scale = 1f / totalWeight;
        m_logger.Log($"Weight总和为{totalWeight}已自动归一化 缩放因子:{scale:F3}");
        return scale;
    }

    /// <summary>
    /// 串行启动所有基础服务
    /// </summary>
    private async UniTask StartServicesAsync(CancellationToken lifetimeToken)
    {
        var accumulated = 0f;

        foreach (var service in m_services)
        {
            lifetimeToken.ThrowIfCancellationRequested();

            // 服务开始时的进度
            ProgressChanged?.Invoke(accumulated, service.Description);

            // 服务执行时的进度
            var normalizedWeight = service.Weight * m_weightScale;
            var tempAccumulated = accumulated;
            var context = new StartupContext(
                lifetimeToken,
                (subProgress, subDesc) =>
                {
                    var clamped = Mathf.Clamp01(subProgress);
                    var globalProgress = tempAccumulated + normalizedWeight * clamped;
                    ProgressChanged?.Invoke(globalProgress, service.Description + "..." + subDesc);
                });

            try
            {
                // 启动服务
                await service.StartAsync(context);
            }
            catch (OperationCanceledException)
            {
                // 启动取消信号
                throw;
            }
            catch (Exception ex)
            {
                m_logger.Error($"基础服务{service.Description}启动异常", ex);

                Failed?.Invoke(service.Description, ex.Message);
                return;
            }

            accumulated += normalizedWeight;
            ProgressChanged?.Invoke(accumulated, service.Description + "完成");
        }

        ProgressChanged?.Invoke(1f, "启动完成");
        Completed?.Invoke();
    }

    public void Dispose()
    {
        if (m_lifetimeToken.IsCancellationRequested)
            return;

        try
        {
            m_lifetimeCancellation.Cancel();
            for (var i = m_services.Count - 1; i >= 0; i--)
            {
                var service = m_services[i];
                service.Dispose();
            }

        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception exception)
        {
            m_logger.Error("基础服务生命周期取消异常", exception);
        }
        finally
        {
            m_lifetimeCancellation.Dispose();   
        }
    }
}
