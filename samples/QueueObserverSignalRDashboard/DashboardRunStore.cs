using System.Collections.Generic;
using System.Linq;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class DashboardRunStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, RunRecord> _runs = new();

    public QueueRunDto UpsertRun(QueueRunDto run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));

        lock (_sync)
        {
            if (_runs.TryGetValue(run.RunId, out var existing))
            {
                existing.Run = run;
                return existing.Run;
            }

            _runs[run.RunId] = new RunRecord(run);
            return run;
        }
    }

    public void AppendEvent(QueueEventDto queueEvent)
    {
        if (queueEvent == null) throw new ArgumentNullException(nameof(queueEvent));

        lock (_sync)
        {
            if (!_runs.TryGetValue(queueEvent.RunId, out var runRecord))
            {
                runRecord = new RunRecord(new QueueRunDto
                {
                    RunId = queueEvent.RunId,
                    Scenario = queueEvent.Scenario,
                    Mode = "unknown",
                    Status = DashboardRunStatus.Running.ToString(),
                    QueueName = null,
                    QueueId = queueEvent.QueueId,
                    StartedUtc = queueEvent.TimestampUtc
                });
                _runs[queueEvent.RunId] = runRecord;
            }

            runRecord.Events.Add(queueEvent);
        }
    }

    public QueueRunDto[] SnapshotRuns()
    {
        lock (_sync)
        {
            return _runs.Values
                .Select(record => record.Run)
                .OrderByDescending(run => run.StartedUtc)
                .ToArray();
        }
    }

    public QueueRunDto? TryGetRun(Guid runId)
    {
        lock (_sync)
        {
            return _runs.TryGetValue(runId, out var record) ? record.Run : null;
        }
    }

    public QueueEventDto[] SnapshotEvents(Guid runId)
    {
        lock (_sync)
        {
            return _runs.TryGetValue(runId, out var record)
                ? record.Events.OrderBy(evt => evt.Sequence).ToArray()
                : Array.Empty<QueueEventDto>();
        }
    }

    private sealed class RunRecord
    {
        public RunRecord(QueueRunDto run)
        {
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public QueueRunDto Run { get; set; }
        public List<QueueEventDto> Events { get; } = new();
    }
}
