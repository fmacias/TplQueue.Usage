using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class PayloadDashboardWorkflowFactory
{
    private const int ExtractDelayMs = 1500;

    private readonly IDataJobFactory _dataJobFactory;
    private readonly ISystemTextJsonUniversalSerializer _jsonSerializer;
    private readonly IXmlUniversalSerializer _xmlSerializer;
    private readonly ILoggerFactory _loggerFactory;

    public PayloadDashboardWorkflowFactory(
        IDataJobFactory dataJobFactory,
        ISystemTextJsonUniversalSerializer jsonSerializer,
        IXmlUniversalSerializer xmlSerializer,
        ILoggerFactory loggerFactory)
    {
        _dataJobFactory = dataJobFactory ?? throw new ArgumentNullException(nameof(dataJobFactory));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        _xmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public PayloadDashboardWorkflow Create(Guid runId, Func<IRetryPolicy> retryPolicyFactory)
    {
        if (retryPolicyFactory == null) throw new ArgumentNullException(nameof(retryPolicyFactory));

        var logger = _loggerFactory.CreateLogger($"SignalRSample.PayloadRun.{runId:N}");
        var extractStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sampleRoot = SamplePaths.ResolveSampleRootPath();
        var inputXmlPath = Path.Combine(sampleRoot, "Data", "greetings.xml");

        var extractPayload = new ExtractGreetingsPayload
        {
            InputXmlPath = inputXmlPath
        };
        var transformPayload = new TransformGreetingsPayload();
        var loadPayload = new LoadGreetingsPayload();

        var extract = _dataJobFactory.DataJob(
            extractPayload,
            new DelegatePayloadHandler<ExtractGreetingsPayload>((payload, ct) =>
                ExtractAsync(payload, transformPayload, logger, extractStarted, ct)),
            name: "Payload Extract");

        var transform = _dataJobFactory.DataJob(
            transformPayload,
            new DelegatePayloadHandler<TransformGreetingsPayload>((payload, ct) =>
                TransformAsync(payload, loadPayload, logger, ct)),
            name: "Payload Transform");

        var load = _dataJobFactory.DataJobRoot(
            loadPayload,
            new DelegatePayloadHandler<LoadGreetingsPayload>((payload, ct) =>
                LoadAsync(payload, logger, ct)),
            name: "Payload Load",
            retryPolicy: retryPolicyFactory);

        load.After(transform);
        transform.After(extract);

        var payloadJobsById = new Dictionary<Guid, IDataJobNode>
        {
            [extract.Id] = extract,
            [transform.Id] = transform,
            [load.Id] = load
        };

        return new PayloadDashboardWorkflow(load, payloadJobsById, extractStarted.Task);
    }

    private async Task ExtractAsync(
        ExtractGreetingsPayload extractPayload,
        TransformGreetingsPayload transformPayload,
        ILogger logger,
        TaskCompletionSource<bool> extractStarted,
        CancellationToken ct)
    {
        if (extractPayload == null) throw new ArgumentNullException(nameof(extractPayload));
        if (transformPayload == null) throw new ArgumentNullException(nameof(transformPayload));
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (extractStarted == null) throw new ArgumentNullException(nameof(extractStarted));

        ct.ThrowIfCancellationRequested();
        extractStarted.TrySetResult(true);

        logger.LogInformation(
            "Payload Extract is reading greetings XML from {InputXmlPath}.",
            extractPayload.InputXmlPath);

        await Task.Delay(ExtractDelayMs, ct).ConfigureAwait(false);

        var xml = await File.ReadAllTextAsync(extractPayload.InputXmlPath, ct).ConfigureAwait(false);
        var document = _xmlSerializer.Deserialize<GreetingsDocument>(xml)
            ?? throw new InvalidOperationException("The greetings XML deserialized to null.");

        extractPayload.Greetings = document.Messages
            .Select(message => new GreetingEnvelope
            {
                Language = message.Language,
                Text = message.Text
            })
            .ToArray();
        extractPayload.GreetingCount = extractPayload.Greetings.Length;

        transformPayload.SourceGreetings = extractPayload.Greetings
            .Select(message => message.Clone())
            .ToArray();
        transformPayload.SourceSnapshotJson = _jsonSerializer.Serialize(extractPayload.Greetings);
    }

    private static Task TransformAsync(
        TransformGreetingsPayload transformPayload,
        LoadGreetingsPayload loadPayload,
        ILogger logger,
        CancellationToken ct)
    {
        if (transformPayload == null) throw new ArgumentNullException(nameof(transformPayload));
        if (loadPayload == null) throw new ArgumentNullException(nameof(loadPayload));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        ct.ThrowIfCancellationRequested();

        transformPayload.Cards = transformPayload.SourceGreetings
            .Select((greeting, index) => new GreetingCard
            {
                Language = greeting.Language,
                Headline = $"{index + 1}. {greeting.Language.ToUpperInvariant()}",
                Body = greeting.Text,
                Sequence = index + 1
            })
            .ToArray();

        transformPayload.RenderingJson = System.Text.Json.JsonSerializer.Serialize(
            transformPayload.Cards.Select(card => new
            {
                card.Language,
                card.Headline,
                card.Body,
                card.Sequence
            }));

        loadPayload.PublishedCards = transformPayload.Cards
            .Select(card => card.Clone())
            .ToArray();

        logger.LogInformation(
            "Payload Transform projected {CardCount} greeting cards.",
            transformPayload.Cards.Length);

        return Task.CompletedTask;
    }

    private static Task LoadAsync(
        LoadGreetingsPayload loadPayload,
        ILogger logger,
        CancellationToken ct)
    {
        if (loadPayload == null) throw new ArgumentNullException(nameof(loadPayload));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        ct.ThrowIfCancellationRequested();

        loadPayload.BroadcastMessages = loadPayload.PublishedCards
            .Select(card => $"Broadcasted {card.Headline} -> {card.Body}")
            .ToArray();
        loadPayload.CompletedUtc = DateTime.UtcNow;

        logger.LogInformation(
            "Payload Load published {MessageCount} greeting messages.",
            loadPayload.BroadcastMessages.Length);

        return Task.CompletedTask;
    }

    internal sealed class PayloadDashboardWorkflow
    {
        public PayloadDashboardWorkflow(
            IDataJobRoot root,
            IReadOnlyDictionary<Guid, IDataJobNode> payloadJobsById,
            Task extractStarted)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            PayloadJobsById = payloadJobsById ?? throw new ArgumentNullException(nameof(payloadJobsById));
            ExtractStarted = extractStarted ?? throw new ArgumentNullException(nameof(extractStarted));
        }

        public IDataJobRoot Root { get; }
        public IReadOnlyDictionary<Guid, IDataJobNode> PayloadJobsById { get; }
        public Task ExtractStarted { get; }
    }

    private sealed class DelegatePayloadHandler<TPayload> : IHandler
        where TPayload : class, IPayload
    {
        private readonly Func<TPayload, CancellationToken, Task> _handleAsync;

        public DelegatePayloadHandler(Func<TPayload, CancellationToken, Task> handleAsync)
        {
            _handleAsync = handleAsync ?? throw new ArgumentNullException(nameof(handleAsync));
        }

        public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
        {
            return _handleAsync((TPayload)payload, cancellationToken);
        }
    }

    public sealed class ExtractGreetingsPayload : IPayload
    {
        public const string HandlerKey = "samples/signalr-dashboard/payload-extract/v1";

        public string InputXmlPath { get; set; } = string.Empty;
        public GreetingEnvelope[] Greetings { get; set; } = Array.Empty<GreetingEnvelope>();
        public int GreetingCount { get; set; }
        public string PayloadId => HandlerKey;
        public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
    }

    public sealed class TransformGreetingsPayload : IPayload
    {
        public const string HandlerKey = "samples/signalr-dashboard/payload-transform/v1";

        public GreetingEnvelope[] SourceGreetings { get; set; } = Array.Empty<GreetingEnvelope>();
        public string SourceSnapshotJson { get; set; } = string.Empty;
        public GreetingCard[] Cards { get; set; } = Array.Empty<GreetingCard>();
        public string RenderingJson { get; set; } = string.Empty;
        public string PayloadId => HandlerKey;
        public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
    }

    public sealed class LoadGreetingsPayload : IPayload
    {
        public const string HandlerKey = "samples/signalr-dashboard/payload-load/v1";

        public GreetingCard[] PublishedCards { get; set; } = Array.Empty<GreetingCard>();
        public string[] BroadcastMessages { get; set; } = Array.Empty<string>();
        public DateTime? CompletedUtc { get; set; }
        public string PayloadId => HandlerKey;
        public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
    }

    public sealed class GreetingEnvelope
    {
        public string Language { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;

        public GreetingEnvelope Clone()
        {
            return new GreetingEnvelope
            {
                Language = Language,
                Text = Text
            };
        }
    }

    public sealed class GreetingCard
    {
        public string Language { get; set; } = string.Empty;
        public string Headline { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public int Sequence { get; set; }

        public GreetingCard Clone()
        {
            return new GreetingCard
            {
                Language = Language,
                Headline = Headline,
                Body = Body,
                Sequence = Sequence
            };
        }
    }

    [System.Xml.Serialization.XmlRoot("greetings")]
    public sealed class GreetingsDocument
    {
        [System.Xml.Serialization.XmlElement("greeting")]
        public List<GreetingMessage> Messages { get; set; } = new List<GreetingMessage>();
    }

    public sealed class GreetingMessage
    {
        [System.Xml.Serialization.XmlAttribute("language")]
        public string Language { get; set; } = string.Empty;

        [System.Xml.Serialization.XmlText]
        public string Text { get; set; } = string.Empty;
    }
}
