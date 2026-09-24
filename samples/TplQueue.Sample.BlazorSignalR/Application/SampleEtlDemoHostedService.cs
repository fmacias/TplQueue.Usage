using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Application;

/// <summary>
/// Starts a deterministic backend-owned workload after all dashboard observers are attached.
/// </summary>
internal sealed class SampleEtlDemoHostedService : BackgroundService
{
    private readonly ISimulationService _simulation;
    private readonly IFifoQ _fifoQueue;
    private readonly IParallelQ _parallelQueue;
    private readonly ICacheQ _cacheQueue;
    private readonly EtlQueueObserver _observer;
    private readonly ILogger<SampleEtlDemoHostedService> _logger;

    public SampleEtlDemoHostedService(
        ISimulationService simulation,
        IFifoQ fifoQueue,
        IParallelQ parallelQueue,
        ICacheQ cacheQueue,
        EtlQueueObserver observer,
        ILogger<SampleEtlDemoHostedService> logger)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _fifoQueue = fifoQueue ?? throw new ArgumentNullException(nameof(fifoQueue));
        _parallelQueue = parallelQueue ?? throw new ArgumentNullException(nameof(parallelQueue));
        _cacheQueue = cacheQueue ?? throw new ArgumentNullException(nameof(cacheQueue));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var fifoSubscription = _fifoQueue.Subscribe(_observer);
        using var parallelSubscription = _parallelQueue.Subscribe(_observer);
        using var cacheSubscription = _cacheQueue.Subscribe(_observer);

        try
        {
            _simulation.Start(stoppingToken);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The hosted ETL demonstration failed.");
            throw;
        }
        finally
        {
            await _simulation.StopAsync().ConfigureAwait(false);
        }
    }
}
