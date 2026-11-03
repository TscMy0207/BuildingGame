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
public class StartupContext
{
    /// <summary>
    /// 基础服务生命周期取消令牌
    /// </summary>
    public CancellationToken LifetimeToken { get; }

    /// <summary>
    /// 上报服务内部进度 参数为0到1的进度和描述
    /// </summary>
    public Action<float, string> ReportProgress { get; }

    /// <summary>
    /// 传递服务生命周期令牌和可选的内部进度回调
    /// </summary>
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
    /// <summary>
    /// 按优先级稳定排序的启动服务列表 同优先级保留注册顺序
    /// </summary>
    private readonly IReadOnlyList<IStartupService> _services;
    /// <summary>
    /// 记录启动失败和取消期间的清理异常
    /// </summary>
    private readonly ILogger<ServiceStartupCoordinator> _logger;
    /// <summary>
    /// 协调器拥有的取消源 在释放时取消并销毁
    /// </summary>
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    /// <summary>
    /// 供全部启动服务共享的协调器生命周期令牌
    /// </summary>
    private readonly CancellationToken _lifetimeToken;

    /// <summary>
    /// 判断启动权重总和是否近似为一的容差
    /// </summary>
    private const float WeightEpsilon = 0.001f;

    /// <summary>
    /// 将服务权重归一化为总体进度比例的缩放系数
    /// </summary>
    private float _weightScale;

    /// <summary>
    /// 通知总体进度与当前启动步骤
    /// </summary>
    public event Action<float, string> ProgressChanged;
    /// <summary>
    /// 通知失败的服务描述和异常原因
    /// </summary>
    public event Action<string, string> Failed;
    /// <summary>
    /// 全部服务启动成功后通知调用方
    /// </summary>
    public event Action Completed;

    /// <summary>
    /// 收集全部启动服务并按优先级稳定排序
    /// </summary>
    public ServiceStartupCoordinator(IEnumerable<IStartupService> services, ILogger<ServiceStartupCoordinator> logger)
    {
        _services = services.AsValueEnumerable().OrderBy(service => service.Priority).ToList();
        _logger = logger;
        _lifetimeToken = _lifetimeCancellation.Token;
    }

    /// <summary>
    /// 检查生命周期并按归一化权重串行启动全部服务
    /// </summary>
    public async UniTask StartAsync()
    {
        if (_lifetimeToken.IsCancellationRequested)
            throw new ObjectDisposedException(nameof(ServiceStartupCoordinator), "基础服务管理器已释放 无法启动基础服务");

        var totalWeight = _services.AsValueEnumerable().Sum(service => service.Weight);
        _weightScale = ComputeWeightScale(totalWeight);

        await StartServicesAsync(_lifetimeToken);
    }

    /// <summary>
    /// 计算权重缩放因子
    /// 权重总和接近1时不需要缩放 否则按总和归一化
    /// </summary>
    private float ComputeWeightScale(float totalWeight)
    {
        if (Math.Abs(totalWeight - 1f) <= WeightEpsilon) return 1f;
        var scale = 1f / totalWeight;
        _logger.Log($"Weight总和为{totalWeight}已自动归一化 缩放因子:{scale:F3}");
        return scale;
    }

    /// <summary>
    /// 串行启动所有基础服务
    /// </summary>
    private async UniTask StartServicesAsync(CancellationToken lifetimeToken)
    {
        var accumulated = 0f;

        foreach (var service in _services)
        {
            lifetimeToken.ThrowIfCancellationRequested();

            // 服务开始时的进度
            ProgressChanged?.Invoke(accumulated, service.Description);

            // 服务执行时的进度
            var normalizedWeight = service.Weight * _weightScale;
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
                _logger.Error($"基础服务{service.Description}启动异常", ex);

                Failed?.Invoke(service.Description, ex.Message);
                return;
            }

            accumulated += normalizedWeight;
            ProgressChanged?.Invoke(accumulated, service.Description + "完成");
        }

        ProgressChanged?.Invoke(1f, "启动完成");
        Completed?.Invoke();
    }

    /// <summary>
    /// 取消服务生命周期并按启动列表逆序释放服务及取消源
    /// </summary>
    public void Dispose()
    {
        if (_lifetimeToken.IsCancellationRequested)
            return;

        try
        {
            _lifetimeCancellation.Cancel();
            for (var i = _services.Count - 1; i >= 0; i--)
            {
                var service = _services[i];
                service.Dispose();
            }

        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.Error("基础服务生命周期取消异常", exception);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }
}
