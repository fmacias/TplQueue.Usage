using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>
/// Terminates the TplQueue observer pipeline by materializing events into the ETL projection.
/// </summary>
internal sealed class EtlQueueObserver : IObserver<IJobEvent>
{
    private readonly IEtlExecutionProjectionStore _store;
    private readonly ILogger<EtlQueueObserver> _logger;

    public EtlQueueObserver(
        IEtlExecutionProjectionStore store,
        ILogger<EtlQueueObserver> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnCompleted()
    {
        _logger.LogInformation("The ETL queue event stream completed.");
    }

    public void OnError(Exception error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));
        _logger.LogError(error, "The ETL queue event stream terminated unexpectedly.");
    }

    public void OnNext(IJobEvent value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        try
        {
            _store.Apply(value);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to project ETL job event {Status} for job {JobId}.",
                value.Status,
                value.JobInfo?.Id);
        }
    }
}
