using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class DashboardObserver : IObserver<IJobEvent>
{
    private readonly QueueEventProjector _projector;
    private readonly DashboardNotifier _notifier;
    private readonly ILogger<DashboardObserver> _logger;

    public DashboardObserver(
        QueueEventProjector projector,
        DashboardNotifier notifier,
        ILogger<DashboardObserver> logger)
    {
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
        _logger.LogError(error, "Observer pipeline failed while projecting queue events.");
    }

    public void OnNext(IJobEvent value)
    {
        var queueEvent = _projector.Project(value);
        _ = _notifier.PublishEventAsync(queueEvent);
    }
}
