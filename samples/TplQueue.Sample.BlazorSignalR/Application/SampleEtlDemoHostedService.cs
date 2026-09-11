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
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(1);
    private readonly ILegacyMeasurementScenario _scenario;
    private readonly IFifoQ _fifoQueue;
    private readonly IParallelQ _parallelQueue;
    private readonly ICacheQ _cacheQueue;
    private readonly EtlQueueObserver _observer;
    private readonly ILogger<SampleEtlDemoHostedService> _logger;

    public SampleEtlDemoHostedService(
        ILegacyMeasurementScenario scenario,
        IFifoQ fifoQueue,
        IParallelQ parallelQueue,
        ICacheQ cacheQueue,
        EtlQueueObserver observer,
        ILogger<SampleEtlDemoHostedService> logger)
    {
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
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
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            SubmitPair(AvailableQueue.Parallel, stoppingToken);
            SubmitPair(AvailableQueue.FIFO, stoppingToken);
            SubmitPair(AvailableQueue.Cache, stoppingToken);

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
    }

    private void SubmitPair(
        AvailableQueue queue,
        CancellationToken cancellationToken)
    {
        _scenario.Run(queue, cancellationToken);
        _scenario.Run(queue, cancellationToken);
    }
}
