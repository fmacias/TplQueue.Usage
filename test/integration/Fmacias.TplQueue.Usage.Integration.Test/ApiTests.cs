using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using static Fmacias.TplQueue.Integration.Test.Cache.CacheFactoryTests;

namespace Fmacias.TplQueue.Integration.Test
{
    [TestFixture]
    public class ApiTests
    {
        private ILoggerFactory _loggerFactory = null!;
        private ILogger<IParallelQ> _logger = null!;
        private Dictionary<string, IQOptions> _queueOptions = null!;
        private Dictionary<string, IRetryPolicyOptions> _retryPolicyOptions = null!;
        private IParallelQ? _parallelQ;

        [SetUp]
        public void SetUp()
        {
            _loggerFactory = NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IParallelQ>();
            _queueOptions = new Dictionary<string, IQOptions>
            {
                { "main", new IntegrationDispatcherOptions(Guid.NewGuid(), maxParallelism: 2, pulseMs: 5, retryPolicy: "no-retry") }
            };
            _retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "no-retry", RetryPolicyOptions.Create(0, 0) }
            };
        }

        [TearDown]
        public void TearDown()
        {
            _parallelQ?.Dispose();
            _loggerFactory?.Dispose();
        }
        /// <summary>
        /// This test ilustrates how to use the cache object from a <see cref="IQ"/>
        /// different than the <see cref="ICacheQ"/>. The <see cref="CacheQ"/>
        /// manages the state of the cache. If you use any other dispatcher, you have to 
        /// manage the states of the cache by your own as in this example.
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task ApiAndCacheFactory_CachedJobInNonCachebleQLogicalUsageTest()
        {
            //Arrange: Payload object and related handler. In this case the handler is shared by both payloads
                var rootPayload = new RecordingPayload
                {
                    Label = "root-label"
                };

                var childPayload = new RecordingPayload
                {
                    Label = "child-label"
                };
                var sharedHandler = new RecordingPayloadHandler();
            
                var rootRegistration = new Helper.HandlerRegistration(
                    rootPayload,
                    sharedHandler
                );
                var childRegistration = new Helper.HandlerRegistration(
                    childPayload,
                    sharedHandler
                );
                var recordingPayloadHandlerResolver = Helper.CreateHandlerResolver(
                    rootRegistration,
                    childRegistration
                );
  
            //ARRANGE: In your client application, dependencies should be just
            //         resolved due to DI
                var api = Helper.GetApi(
                    _retryPolicyOptions,
                    _queueOptions,
                    rootRegistration,
                    childRegistration);
                var observerFactory = api.ObserverFactory();
                var retryAbstractFactory = api.RetryPolicyAbstractFactory;
                var qFactory = api.QFactory;
                var serializer = api.SystemTexSerializerFactory().Serializer();
                var memCache = api.Cache<IMemCache>(
                    MemCacheFactory.Create(),
                    serializer,
                    new TestTypeResolver()
                );
            
                RecordingPayload.Executions.Clear();
                var payloadJobChild = api.DataJobFactory
                    .DataJob(
                        childPayload,
                        recordingPayloadHandlerResolver.Handler(childPayload.PayloadId),
                        "child-name"
                    );

                var payloadJobRoot = api.DataJobFactory
                    .DataJobRoot(
                        rootPayload,
                        recordingPayloadHandlerResolver.Handler(rootPayload.PayloadId),
                        "root-name",
                        () => retryAbstractFactory.PolicyByName("no-retry", _retryPolicyOptions)
                    );
            
                payloadJobRoot.After(payloadJobChild);
           //Arrange: Create Q and a logging dispatcher to track the workflow. 
                using var parallelQ = api.QFactory.GetCoreQ<IParallelQ>("main", Helper.GetLogger<IParallelQ>());
                parallelQ.ResumePolling();
                parallelQ.Subscribe(observerFactory.CreateLoggingObserver(Helper.GetLogger<ILoggingObserver>()));//Should be just ilogger instead, in order to add the same logger used in the queue, for example
            
            //ACT: Append payload job to the cache and get both payload jobs and leased cache entry
               var jobNodes = memCache.Dehydrate(payloadJobRoot, isFifo: true);
                var leased = memCache.TryHydrateNextJob(out var rehidratedRoot, out var lease);
                var rehidratedRootPayload = (RecordingPayload)rehidratedRoot.GetPayload();
                IReadOnlyList<IDataJob> childs = rehidratedRoot.GetDependentDataJobs();
                var rehidratedChild = childs[0];
                var rehidratedChildPayload = (RecordingPayload)rehidratedChild.GetPayload();

            //ASSERT
                Assert.IsInstanceOf<IDataJobRoot>(rehidratedRoot);
                Assert.IsInstanceOf<IDataJob>(rehidratedChild);
                Assert.That(jobNodes[0].Name, Is.EqualTo("child-name"), "Deepest related job first!");
                Assert.That(jobNodes[1].Name, Is.EqualTo("root-name"), "Root job is the las one of the graph of jobs");
                Assert.That(lease.Status, Is.EqualTo(EntryStatus.Pending));
                Assert.That(leased, Is.True);
                Assert.That(rehidratedRoot.Id, Is.EqualTo(payloadJobRoot.Id));
                Assert.That(rehidratedRoot.Name, Is.EqualTo("root-name"));
                Assert.That(rehidratedRootPayload.Label, Is.EqualTo("root-label"));
                Assert.That(rehidratedChild.Id, Is.EqualTo(payloadJobChild.Id));
                Assert.That(rehidratedChild.Name, Is.EqualTo("child-name"));
                Assert.That(rehidratedChildPayload.Label, Is.EqualTo("child-label"));

            //ACT: Start the mechanism for dispatching jobs from the queue.
            //     and add hydrated job root.
                rehidratedRoot.Enqueue(parallelQ, CancellationToken.None);

            //ACT: this example just run rehidrated payload job root
            //     and consecuently removes the corresponding leased entry
            //     Be carefull awainting a job, If youu are not sure what are you doing, do not await.
            await rehidratedRoot.WaitUntilFinishedAsync();       
            //await parallelQ.Wait(1000);
            bool rootSuccessfullyExecuted = rehidratedRoot.ExecutionTime > TimeSpan.MinValue;
            bool childSuccessChildfullyExecuted = rehidratedRoot.Dependencies.First().ExecutionTime > TimeSpan.MinValue;

            //Assert: status of rehidrated childs are run to completion
                Assert.That(rehidratedChild.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(rehidratedRoot.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(rootSuccessfullyExecuted, Is.True);
                Assert.That(childSuccessChildfullyExecuted, Is.True);

            //ACT: Finalize and delete entries from cache
                memCache.AckNode(rehidratedChild.Id, rehidratedChild);
                memCache.AckNode(rehidratedRoot.Id, rehidratedRoot);
                memCache.DeleteRootNode(rehidratedRoot.Id);
            
            //Assert:
                Assert.AreEqual(true, 
                    memCache.GetByJobId(rehidratedChild.Id).Deleted);

                Assert.That(memCache.GetByJobId(rehidratedRoot.Id).Status,
                            Is.EqualTo(EntryStatus.Acknownledged));

            //ACT: Awaiting a enqueued job is posible if neccessary but not required.
                memCache.AckNode(rehidratedChild.Id, rehidratedChild);
                memCache.AckNode(rehidratedRoot.Id, rehidratedRoot);
                memCache.SuccessRootNode(rehidratedRoot.Id);
                CollectionAssert.AreEqual(
                    new[] { "child-label", "root-label" }, 
                    RecordingPayload.Executions.ToArray());

            //ACT
                var childLeaseItem = memCache.GetByJobId(rehidratedChild.Id);

            //ASSERT:
                Assert.That(lease.Status, Is.EqualTo(EntryStatus.Acknownledged));
                Assert.That(childLeaseItem.Status, Is.EqualTo(EntryStatus.Acknownledged));
                Assert.That(lease.RootSuccessed, Is.True);
                Assert.That(childLeaseItem.RootSuccessed, Is.True);
        }

        internal sealed class IntegrationDispatcherOptions : IQOptions
        {
            public IntegrationDispatcherOptions(Guid id, int maxParallelism, int pulseMs, string retryPolicy)
            {
                Id = id;
                MaxParallelism = maxParallelism;
                PulseMs = pulseMs;
                RetryPolicy = retryPolicy;
            }

            public int MaxParallelism { get; }
            public int PulseMs { get; }
            public string RetryPolicy { get; }

            public Guid Id { get; }
        }

        private sealed class RecordingPayload : IPayload
        {
            public static ConcurrentQueue<string> Executions { get; } = new ConcurrentQueue<string>();
            public string Label { get; init; } = string.Empty;
            public string PayloadId => "recording";

            public DateTime CollectionTime => DateTime.UtcNow;
        }
        private sealed class RecordingPayloadHandler : IHandler
        {
            public async Task HandleAsync(IPayload payload, CancellationToken ct)
            {
                await Task.Run(() => {
                    var recordingPayload = (RecordingPayload)payload;
                    RecordingPayload.Executions.Enqueue(recordingPayload.Label);
                }, ct);
            }
        }
    }
}
