using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Jobs;
using Microsoft.Extensions.Logging;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class DashboardRunCoordinator
{
    private const int CollectDelayMs = 1500;
    private const int CancelLeadAfterCollectStartsMs = 250;

    private readonly DashboardNotifier _notifier;
    private readonly IJobFactory _jobFactory;
    private readonly MetadataDashboardQueueRuntime _metadataQueue;
    private readonly PayloadDashboardQueueRuntime _payloadQueue;
    private readonly PayloadDashboardWorkflowFactory _payloadWorkflowFactory;
    private readonly ISystemTextJsonUniversalSerializer _payloadEventSerializer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DashboardRunCoordinator> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _activeRunSync = new();
    private Guid? _activeRunId;

    public DashboardRunCoordinator(
        DashboardNotifier notifier,
        IJobFactory jobFactory,
        MetadataDashboardQueueRuntime metadataQueue,
        PayloadDashboardQueueRuntime payloadQueue,
        PayloadDashboardWorkflowFactory payloadWorkflowFactory,
        ISystemTextJsonUniversalSerializer payloadEventSerializer,
        ILoggerFactory loggerFactory,
        ILogger<DashboardRunCoordinator> logger)
    {
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _jobFactory = jobFactory ?? throw new ArgumentNullException(nameof(jobFactory));
        _metadataQueue = metadataQueue ?? throw new ArgumentNullException(nameof(metadataQueue));
        _payloadQueue = payloadQueue ?? throw new ArgumentNullException(nameof(payloadQueue));
        _payloadWorkflowFactory = payloadWorkflowFactory ?? throw new ArgumentNullException(nameof(payloadWorkflowFactory));
        _payloadEventSerializer = payloadEventSerializer ?? throw new ArgumentNullException(nameof(payloadEventSerializer));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RunStartResult> TryStartRunAsync(
        DashboardRunScenario scenario,
        DashboardRunMode mode,
        CancellationToken ct)
    {
        await _startGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            lock (_activeRunSync)
            {
                if (_activeRunId.HasValue)
                {
                    return RunStartResult.Rejected(
                        $"Run '{_activeRunId.Value}' is still active. Wait until it completes before starting another.");
                }
            }

            var run = new QueueRunDto
            {
                RunId = Guid.NewGuid(),
                Scenario = ScenarioToString(scenario),
                Mode = mode.ToString().ToLowerInvariant(),
                Status = DashboardRunStatus.Starting.ToString(),
                StartedUtc = DateTime.UtcNow
            };

            lock (_activeRunSync)
            {
                _activeRunId = run.RunId;
            }

            await _notifier.PublishRunAsync(run, ct).ConfigureAwait(false);
            _ = Task.Run(() => ExecuteRunAsync(run, scenario, mode), CancellationToken.None);

            return RunStartResult.Accepted(run);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task ExecuteRunAsync(
        QueueRunDto run,
        DashboardRunScenario scenario,
        DashboardRunMode mode)
    {
        using var workflowCancellation = new CancellationTokenSource();

        try
        {
            if (scenario == DashboardRunScenario.Payload)
            {
                await ExecutePayloadRunAsync(run, mode, workflowCancellation).ConfigureAwait(false);
            }
            else
            {
                await ExecuteMetadataRunAsync(run, mode, workflowCancellation).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR dashboard sample run {RunId} failed.", run.RunId);
            await _notifier.PublishRunAsync(
                CloneRun(
                    run,
                    DashboardRunStatus.Failed,
                    run.QueueName,
                    run.QueueId,
                    DateTime.UtcNow,
                    ex.Message)).ConfigureAwait(false);
        }
        finally
        {
            lock (_activeRunSync)
            {
                if (_activeRunId == run.RunId)
                {
                    _activeRunId = null;
                }
            }
        }
    }

    private async Task ExecuteMetadataRunAsync(
        QueueRunDto run,
        DashboardRunMode mode,
        CancellationTokenSource workflowCancellation)
    {
        var queue = _metadataQueue.Queue;
        var projector = new QueueEventProjector(
            run.RunId,
            run.Scenario,
            queue.QueueId);
        using var subscription = queue.Subscribe(
            new DashboardObserver(
                projector,
                _notifier,
                _loggerFactory.CreateLogger<DashboardObserver>()));

        await _notifier.PublishRunAsync(
            CloneRun(
                run,
                DashboardRunStatus.Running,
                queue.Name,
                queue.QueueId,
                completedUtc: null,
                failureMessage: null)).ConfigureAwait(false);

        var state = new PipelineState();
        var context = new PipelineContext(_loggerFactory.CreateLogger($"SignalRSample.Run.{run.RunId:N}"));
        var helperOperation = new LocalHelperOperation(context.Logger);

        var collect = _jobFactory.Job(
            CollectDashboardRowsAsync,
            state,
            context,
            name: "CollectDashboardRows");

        var transform = _jobFactory.Job(
            TransformDashboardRows,
            state,
            context,
            name: "TransformDashboardRows");

        var publish = _jobFactory.JobRoot(
            PublishDashboardSummary,
            state,
            context,
            _metadataQueue.CreateRetryPolicyFactory(),
            name: "PublishDashboardSummary");

        collect.Then(transform).Then(publish);

        queue.Enqueue(publish, workflowCancellation.Token);
        queue.Enqueue(
            cancellationToken => helperOperation.ExecuteAsync(cancellationToken),
            CancellationToken.None,
            "Refresh sidebar");

        if (mode == DashboardRunMode.Cancel)
        {
            await CancelDuringCollectAsync(state, queue, workflowCancellation, context.Logger).ConfigureAwait(false);
            await _notifier.PublishRunAsync(
                CloneRun(
                    run,
                    DashboardRunStatus.Canceled,
                    queue.Name,
                    queue.QueueId,
                    DateTime.UtcNow,
                    null)).ConfigureAwait(false);
        }
        else
        {
            await queue.Wait().ConfigureAwait(false);
            await _notifier.PublishRunAsync(
                CloneRun(
                    run,
                    DashboardRunStatus.Completed,
                    queue.Name,
                    queue.QueueId,
                    DateTime.UtcNow,
                    null)).ConfigureAwait(false);
        }
    }

    private async Task ExecutePayloadRunAsync(
        QueueRunDto run,
        DashboardRunMode mode,
        CancellationTokenSource workflowCancellation)
    {
        var queue = _payloadQueue.Queue;
        var workflow = _payloadWorkflowFactory.Create(
            run.RunId,
            _payloadQueue.CreateRetryPolicyFactory());
        var projector = new QueueEventProjector(
            run.RunId,
            run.Scenario,
            queue.QueueId,
            payloadSerializer: _payloadEventSerializer,
            payloadJobsById: workflow.PayloadJobsById,
            payloadCaptureMode: QueueEventPayloadCaptureMode.TerminalOnly);
        using var subscription = queue.Subscribe(
            new DashboardObserver(
                projector,
                _notifier,
                _loggerFactory.CreateLogger<DashboardObserver>()));

        await _notifier.PublishRunAsync(
            CloneRun(
                run,
                DashboardRunStatus.Running,
                queue.Name,
                queue.QueueId,
                completedUtc: null,
                failureMessage: null)).ConfigureAwait(false);

        queue.Enqueue(workflow.Root, workflowCancellation.Token);

        if (mode == DashboardRunMode.Cancel)
        {
            await CancelDuringPayloadExtractAsync(
                workflow.ExtractStarted,
                queue,
                workflowCancellation,
                _loggerFactory.CreateLogger($"SignalRSample.PayloadRun.{run.RunId:N}")).ConfigureAwait(false);
            await _notifier.PublishRunAsync(
                CloneRun(
                    run,
                    DashboardRunStatus.Canceled,
                    queue.Name,
                    queue.QueueId,
                    DateTime.UtcNow,
                    null)).ConfigureAwait(false);
        }
        else
        {
            await queue.Wait().ConfigureAwait(false);
            await _notifier.PublishRunAsync(
                CloneRun(
                    run,
                    DashboardRunStatus.Completed,
                    queue.Name,
                    queue.QueueId,
                    DateTime.UtcNow,
                    null)).ConfigureAwait(false);
        }
    }

    private static async Task CollectDashboardRowsAsync(
        CancellationToken ct,
        PipelineState state,
        PipelineContext context)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (context == null) throw new ArgumentNullException(nameof(context));

        ct.ThrowIfCancellationRequested();
        state.CollectStarted.TrySetResult(true);

        context.Logger.LogInformation(
            "CollectDashboardRows is simulating a slow source read for {DelayMs} ms.",
            CollectDelayMs);

        await Task.Delay(CollectDelayMs, ct).ConfigureAwait(false);

        state.Rows = new[]
        {
            new DashboardRow("Ada", "Accepted", 1),
            new DashboardRow("Linus", "Queued", 2),
            new DashboardRow("Grace", "Dispatched", 3)
        };
    }

    private static void TransformDashboardRows(
        CancellationToken ct,
        PipelineState state,
        PipelineContext context)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (context == null) throw new ArgumentNullException(nameof(context));

        ct.ThrowIfCancellationRequested();

        var rows = state.Rows
            ?? throw new InvalidOperationException("CollectDashboardRows must populate the dashboard rows before transformation.");

        state.Summary = rows
            .Select(row => $"{row.Customer}:{row.Stage}:{row.Sequence}")
            .ToArray();

        context.Logger.LogInformation(
            "TransformDashboardRows projected {RowCount} dashboard rows.",
            state.Summary.Length);
    }

    private static void PublishDashboardSummary(
        CancellationToken ct,
        PipelineState state,
        PipelineContext context)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (context == null) throw new ArgumentNullException(nameof(context));

        ct.ThrowIfCancellationRequested();

        var summary = state.Summary
            ?? throw new InvalidOperationException("TransformDashboardRows must populate the summary before publication.");

        context.Logger.LogInformation(
            "PublishDashboardSummary completed with {ProjectionCount} rendered rows.",
            summary.Length);
    }

    private static async Task CancelDuringCollectAsync(
        PipelineState state,
        IQ queue,
        CancellationTokenSource workflowCancellation,
        ILogger logger)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (queue == null) throw new ArgumentNullException(nameof(queue));
        if (workflowCancellation == null) throw new ArgumentNullException(nameof(workflowCancellation));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        await state.CollectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        logger.LogWarning(
            "Cancellation will be requested {DelayMs} ms after CollectDashboardRows starts.",
            CancelLeadAfterCollectStartsMs);

        await Task.Delay(CancelLeadAfterCollectStartsMs).ConfigureAwait(false);

        logger.LogWarning("Canceling the dashboard pipeline during CollectDashboardRows.");
        workflowCancellation.Cancel();
        await queue.Wait().ConfigureAwait(false);
    }

    private static async Task CancelDuringPayloadExtractAsync(
        Task extractStarted,
        IQ queue,
        CancellationTokenSource workflowCancellation,
        ILogger logger)
    {
        if (extractStarted == null) throw new ArgumentNullException(nameof(extractStarted));
        if (queue == null) throw new ArgumentNullException(nameof(queue));
        if (workflowCancellation == null) throw new ArgumentNullException(nameof(workflowCancellation));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        await extractStarted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        logger.LogWarning(
            "Cancellation will be requested {DelayMs} ms after Payload Extract starts.",
            CancelLeadAfterCollectStartsMs);

        await Task.Delay(CancelLeadAfterCollectStartsMs).ConfigureAwait(false);

        logger.LogWarning("Canceling the payload dashboard pipeline during Payload Extract.");
        workflowCancellation.Cancel();
        await queue.Wait().ConfigureAwait(false);
    }

    private static QueueRunDto CloneRun(
        QueueRunDto current,
        DashboardRunStatus status,
        string? queueName,
        Guid? queueId,
        DateTime? completedUtc,
        string? failureMessage)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));

        return new QueueRunDto
        {
            RunId = current.RunId,
            Scenario = current.Scenario,
            Mode = current.Mode,
            Status = status.ToString(),
            QueueName = queueName ?? current.QueueName,
            QueueId = queueId ?? current.QueueId,
            StartedUtc = current.StartedUtc,
            CompletedUtc = completedUtc,
            FailureMessage = failureMessage
        };
    }

    private static string ScenarioToString(DashboardRunScenario scenario)
    {
        return scenario == DashboardRunScenario.Payload
            ? "payload"
            : "metadata";
    }

    private sealed class PipelineState
    {
        public TaskCompletionSource<bool> CollectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DashboardRow[]? Rows { get; set; }
        public string[]? Summary { get; set; }
    }

    private sealed class LocalHelperOperation
    {
        private readonly ILogger _logger;

        public LocalHelperOperation(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct).ConfigureAwait(false);
            _logger.LogInformation("Refresh sidebar completed as an independent queued helper task.");
        }
    }

    private sealed class PipelineContext
    {
        public PipelineContext(ILogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ILogger Logger { get; }
    }

    private sealed class DashboardRow
    {
        public DashboardRow(string customer, string stage, int sequence)
        {
            Customer = customer ?? throw new ArgumentNullException(nameof(customer));
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Sequence = sequence;
        }

        public string Customer { get; }
        public string Stage { get; }
        public int Sequence { get; }
    }

    internal sealed class RunStartResult
    {
        private RunStartResult(bool started, QueueRunDto? run, string? message)
        {
            Started = started;
            Run = run;
            Message = message;
        }

        public bool Started { get; }
        public QueueRunDto? Run { get; }
        public string? Message { get; }

        public static RunStartResult Accepted(QueueRunDto run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            return new RunStartResult(true, run, null);
        }

        public static RunStartResult Rejected(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentNullException(nameof(message));
            return new RunStartResult(false, null, message);
        }
    }
}
