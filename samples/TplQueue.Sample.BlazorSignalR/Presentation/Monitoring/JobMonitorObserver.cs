using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>
/// Synchronously captures each runtime event at the observer boundary and forwards only its detached snapshot.
/// </summary>
internal sealed class JobMonitorObserver : IObserver<IJobEvent>
{
    private readonly IJobMonitorStore _store;
    private readonly ILogger<JobMonitorObserver> _logger;
    private readonly IObserverFactory _observerFactory;

    public JobMonitorObserver(
        IJobMonitorStore store,
        ILogger<JobMonitorObserver> logger,
        IObserverFactory observerFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observerFactory = observerFactory ?? throw new ArgumentNullException(nameof(observerFactory));
    }

    public void OnCompleted()
    {
        _logger.LogInformation("The queue event stream completed.");
    }

    public void OnError(Exception error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));
        _logger.LogError(error, "The queue event stream terminated unexpectedly.");
    }

    public void OnNext(IJobEvent value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        _store.Apply(_observerFactory.CreateJobEventSnapshot(value));
    }
}
