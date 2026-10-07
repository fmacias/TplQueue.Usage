using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Application;

/// <summary>Runs the sample workflows for the host lifetime, observing the shared runtime before delivery starts.</summary>
internal sealed class SampleEtlDemoHostedService : BackgroundService
{
    private readonly ISimulationService _simulation;
    private readonly EtlQueueObserver _observer;
    private readonly ILogger<SampleEtlDemoHostedService> _logger;

    public SampleEtlDemoHostedService(
        ISimulationService simulation,
        EtlQueueObserver observer,
        ILogger<SampleEtlDemoHostedService> logger)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = _simulation.Subscribe(_observer);
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
