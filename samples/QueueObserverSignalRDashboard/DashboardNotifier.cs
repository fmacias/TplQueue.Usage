using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class DashboardNotifier
{
    private readonly DashboardRunStore _store;
    private readonly IHubContext<QueueEventsHub> _hubContext;
    private readonly ILogger<DashboardNotifier> _logger;

    public DashboardNotifier(
        DashboardRunStore store,
        IHubContext<QueueEventsHub> hubContext,
        ILogger<DashboardNotifier> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishRunAsync(QueueRunDto run, CancellationToken ct = default)
    {
        var snapshot = _store.UpsertRun(run);
        await BroadcastSafelyAsync("runUpdated", snapshot, ct).ConfigureAwait(false);
    }

    public async Task PublishEventAsync(QueueEventDto queueEvent, CancellationToken ct = default)
    {
        _store.AppendEvent(queueEvent);
        await BroadcastSafelyAsync("jobEvent", queueEvent, ct).ConfigureAwait(false);
    }

    private async Task BroadcastSafelyAsync(string method, object dto, CancellationToken ct)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(method, dto, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR broadcast failed for method {Method}.", method);
        }
    }
}
