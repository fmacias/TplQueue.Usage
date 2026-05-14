using Fmacias.TplQueue;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.RetryPolicies;
using log4net;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Xml.Serialization;

namespace TplQueue.Usage.QueueObserverConsole
{
    internal static class Program
    {
        private const int ExtractSimulationDelayMs = 1500;
        private const int CancelLeadAfterExtractStartsMs = 250;
        private const string LogRootPropertyName = "LogRoot";

        private static async Task<int> Main(string[] args)
        {
            try
            {
                var executionMode = ParseExecutionMode(args);
                using var workflowCancellation = new CancellationTokenSource();
                var logRoot = PrepareLogRoot();
                GlobalContext.Properties[LogRootPropertyName] = logRoot;

                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddLog4Net("log4net.config");
                });

                ILogger appLogger = loggerFactory.CreateLogger("TplQueue.Usage.QueueObserverConsole");
                ILogger<IParallelQ> queueLogger = loggerFactory.CreateLogger<IParallelQ>();
                ILogger<IProfilingObserver> profilingLogger = loggerFactory.CreateLogger<IProfilingObserver>();
                ILogger<ILoggingObserver> observerLogger = loggerFactory.CreateLogger<ILoggingObserver>();

                appLogger.LogDebug("LoggerFactory initialized with log4net output in {LogRoot}.", logRoot);
                appLogger.LogInformation("Execution mode: {ExecutionMode}", executionMode.ToString().ToLowerInvariant());

                var api = API.Create(
                    CoreApi.Create(),
                    new Dictionary<string, IRetryPolicyOptions>(),
                    new Dictionary<string, IQOptions>());

                Func<IRetryPolicy> retryPolicyFactory = () => api.RetryPolicy(
                    ExponentialBackoffFactory.Create(),
                    maxRetries: 3,
                    delayMs: 250,
                    factor: 2d);

                using IParallelQ queue = api.QFactory.Parallel(
                    Guid.NewGuid(),
                    "greetings-pipeline",
                    maxParallelism: 1,
                    queueLogger,
                    retryPolicyFactory);
                    
                queueLogger.LogInformation(
                    "Queue '{QueueName}' created with max parallelism {MaxParallelism}.",
                    queue.Name,
                    queue.MaxParallelism);

                IObserverFactory observerFactory = api.ObserverFactory();
                using IDisposable loggingSubscription = queue.Subscribe(observerFactory.CreateLoggingObserver(observerLogger));
                using IDisposable profilingSubscription = queue.Subscribe(observerFactory.CreateProfilingObserver(profilingLogger));
                observerLogger.LogInformation("Logging observer subscribed to queue '{QueueName}'.", queue.Name);
                profilingLogger.LogInformation("Profiling observer subscribed to queue '{QueueName}'.", queue.Name);

                var state = new PipelineState();
                var context = new PipelineContext(
                    Path.Combine(AppContext.BaseDirectory, "Data", "greetings.xml"),
                    api.XmlSerializerFactory().Serializer(),
                    api.SystemTextSerializerFactory().Serializer(
                        new JsonSerializerOptions { WriteIndented = true }),
                    appLogger);
                var localHelperOperation = new LocalHelperOperation(appLogger);

                var extract = api.JobFactory.Job(
                    ExtractAsync,
                    state,
                    context,
                    name: "Extract");

                var transform = api.JobFactory.Job(
                    Transform,
                    state,
                    context,
                    name: "Transform");
                
                var load = api.JobFactory.JobRoot(
                    Load,
                    state,
                    context,
                    retryPolicyFactory,
                    name: "Load");
        
                extract.Then(transform).Then(load);

                appLogger.LogDebug("Workflow graph composed: Extract -> Transform -> Load.");
                appLogger.LogInformation("Input XML: {InputXmlPath}", context.InputXmlPath);
                appLogger.LogInformation(
                    "The sample enqueues the job graph and a standalone helper task into the same queue dispatcher.");

                queue.Enqueue(load, workflowCancellation.Token);
                queue.Enqueue(
                    ct => localHelperOperation.ExecuteAsync(ct),
                    CancellationToken.None,
                    "Standalone helper task");

                appLogger.LogDebug("Workflow graph and standalone helper task enqueued.");

                await ExecuteRequestedModeAsync(
                    executionMode,
                    state,
                    queue,
                    workflowCancellation,
                    appLogger).ConfigureAwait(false);

                appLogger.LogInformation(
                    "Sample finished after queue finalization in mode '{ExecutionMode}'.",
                    executionMode.ToString().ToLowerInvariant());

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static string PrepareLogRoot()
        {
            var sampleRoot = ResolveSampleRootPath();
            var logRoot = Path.Combine(sampleRoot, "Logs");
            Directory.CreateDirectory(logRoot);
            return logRoot;
        }

        private static string ResolveSampleRootPath()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "QueueObserverConsole.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return AppContext.BaseDirectory;
        }

        private static async Task ExtractAsync(CancellationToken ct, PipelineState state, PipelineContext context)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (context == null) throw new ArgumentNullException(nameof(context));

            ct.ThrowIfCancellationRequested();
            state.ExtractStarted.TrySetResult(true);

            context.Logger.LogInformation("Extract is reading XML from {InputXmlPath}.", context.InputXmlPath);
            context.Logger.LogDebug(
                "Extract is simulating slow I/O for {DelayMs} ms so wait and cancel modes can be observed deterministically.",
                ExtractSimulationDelayMs);

            await Task.Delay(ExtractSimulationDelayMs, ct).ConfigureAwait(false);

            var xml = await File.ReadAllTextAsync(context.InputXmlPath, ct).ConfigureAwait(false);
            state.Greetings = context.XmlSerializer.Deserialize<GreetingsDocument>(xml)
                ?? throw new InvalidOperationException("XML deserialization returned null.");
        }

        private static void Transform(CancellationToken ct, PipelineState state, PipelineContext context)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (context == null) throw new ArgumentNullException(nameof(context));

            ct.ThrowIfCancellationRequested();

            var greetings = state.Greetings
                ?? throw new InvalidOperationException("Extract must populate the greetings document before Transform runs.");

            context.Logger.LogInformation(
                "Transform is serializing {GreetingCount} greetings to JSON.",
                greetings.Messages.Count);

            state.JsonPayload = context.JsonSerializer.Serialize(greetings);
        }

        private static void Load(CancellationToken ct, PipelineState state, PipelineContext context)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (context == null) throw new ArgumentNullException(nameof(context));

            ct.ThrowIfCancellationRequested();

            var jsonPayload = state.JsonPayload;
            if (string.IsNullOrWhiteSpace(jsonPayload))
            {
                throw new InvalidOperationException("Transform must populate the JSON payload before Load runs.");
            }

            context.Logger.LogInformation("Load is writing the serialized JSON payload to the console.");
            Console.WriteLine();
            Console.WriteLine("Serialized JSON output:");
            Console.WriteLine(jsonPayload);
            Console.WriteLine();
        }

        private static SampleExecutionMode ParseExecutionMode(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return SampleExecutionMode.Wait;
            }

            if (args.Length > 1)
            {
                throw new ArgumentException("QueueObserverConsole accepts at most one parameter: 'wait' or 'cancel'.");
            }

            if (string.Equals(args[0], "wait", StringComparison.OrdinalIgnoreCase))
            {
                return SampleExecutionMode.Wait;
            }

            if (string.Equals(args[0], "cancel", StringComparison.OrdinalIgnoreCase))
            {
                return SampleExecutionMode.Cancel;
            }

            throw new ArgumentException("Unsupported execution mode. Use 'wait' or 'cancel'.");
        }

        private static Task ExecuteRequestedModeAsync(
            SampleExecutionMode executionMode,
            PipelineState state,
            IQ queue,
            CancellationTokenSource workflowCancellation,
            ILogger logger)
        {
            return executionMode == SampleExecutionMode.Cancel
                ? CancelDuringExtractAsync(state, queue, workflowCancellation, logger)
                : WaitForQueueFinalizationAsync(queue, logger);
        }

        private static async Task CancelDuringExtractAsync(
            PipelineState state,
            IQ queue,
            CancellationTokenSource workflowCancellation,
            ILogger logger)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (queue == null) throw new ArgumentNullException(nameof(queue));
            if (workflowCancellation == null) throw new ArgumentNullException(nameof(workflowCancellation));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            await state.ExtractStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            logger.LogInformation(
                "Extract has started; cancellation will be requested in {DelayMs} ms.",
                CancelLeadAfterExtractStartsMs);

            await Task.Delay(CancelLeadAfterExtractStartsMs).ConfigureAwait(false);

            logger.LogWarning("Canceling the workflow during Extract.");
            workflowCancellation.Cancel();
            await WaitForQueueFinalizationAsync(queue, logger).ConfigureAwait(false);
        }

        private static async Task WaitForQueueFinalizationAsync(IQ q, ILogger logger)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            
            await q.Wait().ConfigureAwait(false);
            logger.LogInformation("Queue finalized gracefully.");
        }

        private enum SampleExecutionMode
        {
            Wait,
            Cancel
        }

        private sealed class PipelineState
        {
            public TaskCompletionSource<bool> ExtractStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public GreetingsDocument? Greetings { get; set; }
            public string? JsonPayload { get; set; }
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
                _logger.LogInformation("Standalone helper operation executed outside the job graph.");
            }
        }

        private sealed class PipelineContext
        {
            public PipelineContext(
                string inputXmlPath,
                IXmlUniversalSerializer xmlSerializer,
                IUniversalDataSerializer jsonSerializer,
                ILogger logger)
            {
                InputXmlPath = inputXmlPath ?? throw new ArgumentNullException(nameof(inputXmlPath));
                XmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
                JsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
                Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public string InputXmlPath { get; }
            public IXmlUniversalSerializer XmlSerializer { get; }
            public IUniversalDataSerializer JsonSerializer { get; }
            public ILogger Logger { get; }
        }
    }

    [XmlRoot("greetings")]
    public sealed class GreetingsDocument
    {
        [XmlElement("greeting")]
        public List<GreetingMessage> Messages { get; set; } = new List<GreetingMessage>();
    }

    public sealed class GreetingMessage
    {
        [XmlAttribute("language")]
        public string Language { get; set; } = string.Empty;

        [XmlText]
        public string Text { get; set; } = string.Empty;
    }
}
