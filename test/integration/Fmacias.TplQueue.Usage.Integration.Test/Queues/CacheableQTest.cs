using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;
using static Fmacias.TplQueue.Integration.Test.Cache.CacheFactoryTests;

namespace Fmacias.TplQueue.Integration.Test.Queues
{
    [TestFixture()]
    public class CacheableQTest
    {
        private ICacheQ _queue = null!;
        private IMemCache _memCache = null!;
        private IDisposable _loggingObserverUnsubscriber = null!;
        private ILogger<ICacheQ> _logger = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
        private IQFactoryAdapter _queueFactory = null!;
        private IObserverFactory _observerFactory = null!;
        private IDataJobFactory _payloadJobFactory = null!;
        private IUniversalDataSerializer _universalPayloadSerializer = null!;
        private IPayloadHandlers _handlerResolver = null!;
        public class DummyPayload1000 : IPayload
        {
            private readonly DateTime _collectionTime;

            public DummyPayload1000()
            {
                _collectionTime = DateTime.UtcNow;
            }

            public string PayloadId { get; init; } = "dummy-1000-handler";
            public bool Executed { get; init; }
            public string? Greating { get; init; }
            public DateTime CollectionTime => _collectionTime;
        }

        public class DummyPayload : IPayload
        {
            private readonly DateTime _collectionTime = DateTime.UtcNow;

            public string PayloadId { get; init; } = "dummy-handler";
            public bool Executed { get; init; }
            public string? Greating { get; init; }

            public DateTime CollectionTime => _collectionTime;
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>{
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var queueOptions = new Dictionary<string, IQOptions>();
            var api = Helper.GetApi(retryPolicyOptions, queueOptions);
            _logger = Helper.GetLogger<ICacheQ>();
            _retryPolicyFactory = api.RetryPolicyAbstractFactory;
            _queueFactory = api.QFactory;
            _observerFactory = api.ObserverFactory();
            _handlerResolver = Helper.CreateHandlerResolver(
                new Helper.HandlerRegistration(
                    new DummyPayload(), 
                    DelegatePayloadHandler.Create((payload, ct) =>
                    {
                        return Task.CompletedTask;
                    })),
                new Helper.HandlerRegistration(
                    new DummyPayload1000(), 
                    DelegatePayloadHandler.Create(async (payload, ct) =>
                    {
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                    })));
            _payloadJobFactory = api.DataJobFactory;
            _universalPayloadSerializer = Helper.CreatePayloadSerializer();
        }

        [SetUp]
        public void Setup()
        {
            var defaultQueue = _queueFactory.Parallel(
                Guid.NewGuid(), name: "Default test-TaskDipatcher",
                maxParallelism: 8,
                logger: _logger,
                retryPolicyFactory: () =>
                _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var loggingObserver = _observerFactory
                .CreateLoggingObserver(Helper.GetLogger<ILoggingObserver>());

            _memCache = MemCacheFactory.Create()
                .CreateCache(_universalPayloadSerializer, _payloadJobFactory, new TestTypeResolver(), _handlerResolver, _retryPolicyFactory);

            _queue = _queueFactory.CacheQ(
                logger: _logger,
                payloadLeaseCache: _memCache,
                defaultQueue);
            
            _loggingObserverUnsubscriber = _queue.Subscribe(loggingObserver);
        }

        [TearDown]
        public void TearDown()
        {
            _queue.Dispose();
            _loggingObserverUnsubscriber.Dispose();
            _memCache = null!;
        }

        [Test]
        public void Enqueue_AppendsToCache_NoStarted()
        {
            var rootPayload = new DummyPayload();
            var childPayload = new DummyPayload();
            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());
            
            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");
            
            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);
            
            _queue.Enqueue(root, CancellationToken.None);

            //Wait a moment to handle status due to callback of queue internally.
            Task.Delay(50).Wait();          
            var rootEntry = _memCache.GetByJobId(root.Id);

            AssertCacheLeaseEntry(
                entry: rootEntry, 
                payloadCarrierRunner: root,
                isRoot: true,
                status:EntryStatus.Pending, //Only root have the state cached. Semantically a root object is enqueued, and dequeued.
                isFifo:false,
                jobRootId: root.Id,
                parentJobId:Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:false);

            var childEntry = _memCache.GetByJobId(child.Id);
            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Pending,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted: false);
        }

        [Test]
        public async Task Enqueue_AppendsToCache_Started()
        {
            var rootPayload = new DummyPayload();
            var childPayload = new DummyPayload();
        
            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");
            
            root.After(child);
            var rootPayloadJson = root.Serialize(_universalPayloadSerializer);
            var childPayloadJson = child.Serialize(_universalPayloadSerializer);

            _queue.Enqueue(root, CancellationToken.None);

            var rootEntry = _memCache.GetByJobId(root.Id);
            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Pending,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted: false);

            var childEntry = _memCache.GetByJobId(child.Id);

            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Pending,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted:false);
            
            _queue.ResumePolling();
            await _queue.Wait();
            rootEntry = WaitForEntryStatus(root.Id, EntryStatus.Acknownledged);
            childEntry = WaitForEntryStatus(child.Id, EntryStatus.Acknownledged);

            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Acknownledged,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: null,
                nameJson: rootPayloadJson,
                entryDeleted:true);


            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Acknownledged,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: null,
                nameJson: childPayloadJson,
                entryDeleted:true);
        }
        [Test]
        public void EnqueueFifo_NoStarted_Fifo()
        {
            var rootPayload = new DummyPayload();
            var childPayload = new DummyPayload();

            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");

            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);

            _queue.EnqueueFifo(root, CancellationToken.None);
            Task.Delay(1000).Wait();

            var rootEntry = _memCache.GetByJobId(root.Id);
            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Pending, //Only root have the state cached. Semantically a root object is enqueued, and dequeued.
                isFifo: true,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:false);

            var childEntry = _memCache.GetByJobId(child.Id);
            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Pending,
                isFifo: false, // property is only relevant on root objects.
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted:false);
        }

        [Test]
        public void EnqueueFifo_Start_Fifo()
        {
            var rootPayload = new DummyPayload();
            var childPayload = new DummyPayload();

            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");

            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);

            _queue.EnqueueFifo(root, CancellationToken.None);

            var rootEntry = _memCache.GetByJobId(root.Id);

            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Pending, //Only root have the state cached. Semantically a root object is enqueued, and dequeued.
                isFifo: true,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:false);

            var childEntry = _memCache.GetByJobId(child.Id);
            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Pending,
                isFifo: false, // property is only relevant on root objects.
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted: false);

            _queue.ResumePolling();
            Task.Delay(2000).Wait();
            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Acknownledged,
                isFifo: true,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: null,
                nameJson: rootPayloadJson,
                entryDeleted:true);

            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Acknownledged,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: null,
                nameJson: childPayloadJson,
                entryDeleted:true);
        }
        [Test]
        public void EnqueueFifo_Start_CancelBeforeStart_Fifo()
        {
            var rootPayload = new DummyPayload();
            var childPayload = new DummyPayload();

            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");

            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);

            var cancellationTockenSource = new CancellationTokenSource();
            _queue.EnqueueFifo(root, cancellationTockenSource.Token);
            var rootEntry = _memCache.GetByJobId(root.Id);
            var childEntry = _memCache.GetByJobId(child.Id);

            cancellationTockenSource.Cancel();
            _queue.ResumePolling();
            Task.Delay(1000).Wait();

            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Canceled,
                isFifo: true,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:true);

            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Canceled,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted:true);
        }

        [Test]
        public void EnqueueFifo_Start_CancelBeforeStart_NonFifo()
        {
            var rootPayload = new DummyPayload();
            var childPayload = new DummyPayload();

            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");

            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);

            var cancellationTockenSource = new CancellationTokenSource();
            _queue.Enqueue(root, cancellationTockenSource.Token);
            var rootEntry = _memCache.GetByJobId(root.Id);
            var childEntry = _memCache.GetByJobId(child.Id);

            cancellationTockenSource.Cancel();
            _queue.ResumePolling();
            Task.Delay(1000).Wait();

            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Canceled,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:true);

            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Canceled,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted:true);
        }

        [Test]
        public async Task EnqueueFifo_Start_CancelDuringExecution_Fifo()
        {
            var rootPayload = new DummyPayload1000();
            var childPayload = new DummyPayload();

            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");

            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);
            var cts = new CancellationTokenSource();
            _queue.LeasingPulseMs = 100;
            _queue.EnqueueFifo(root, cts.Token);
            
            var rootEntry = _memCache.GetByJobId(root.Id);

            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Pending, //Only root have the state cached. Semantically a root object is enqueued, and dequeued.
                isFifo: true,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:false);

            var childEntry = _memCache.GetByJobId(child.Id);
            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Pending,
                isFifo: false, // property is only relevant on root objects.
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted:false);
            
            _queue.ResumePolling();
            cts.CancelAfter(500);
            await Task.Delay(1100);
            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Canceled,
                isFifo: true,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: null,
                nameJson: rootPayloadJson,
                entryDeleted: true);

            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Acknownledged,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: null,
                nameJson: childPayloadJson,
                entryDeleted:true);
       
            cts.Dispose();
        }
        [Test]
        public async Task EnqueueFifo_Start_CancelDuringExecution_NonFifo()
        {
            var rootPayload = new DummyPayload1000();
            var childPayload = new DummyPayload();

            var root = _payloadJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-job",
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());

            var child = _payloadJobFactory.DataJob(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                "Child payload job");

            root.After(child);
            var rootPayloadJson = SerializePayload(rootPayload);
            var childPayloadJson = SerializePayload(childPayload);
            var cts = new CancellationTokenSource();
            _queue.LeasingPulseMs = 100;
            _queue.Enqueue(root, cts.Token);

            var rootEntry = _memCache.GetByJobId(root.Id);

            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Pending, //Only root have the state cached. Semantically a root object is enqueued, and dequeued.
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: rootPayloadJson,
                nameJson: rootPayloadJson,
                entryDeleted:false);

            var childEntry = _memCache.GetByJobId(child.Id);
            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Pending,
                isFifo: false, // property is only relevant on root objects.
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: childPayloadJson,
                nameJson: childPayloadJson,
                entryDeleted:false);

            _queue.ResumePolling();
            cts.CancelAfter(500);
            await Task.Delay(1100);
            AssertCacheLeaseEntry(
                entry: rootEntry,
                payloadCarrierRunner: root,
                isRoot: true,
                status: EntryStatus.Canceled,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: Guid.Empty,
                payloadJson: null,
                nameJson: rootPayloadJson,
                entryDeleted: true);

            AssertCacheLeaseEntry(
                entry: childEntry,
                payloadCarrierRunner: child,
                isRoot: false,
                status: EntryStatus.Acknownledged,
                isFifo: false,
                jobRootId: root.Id,
                parentJobId: root.Id,
                payloadJson: null,
                nameJson: childPayloadJson,
                entryDeleted:true);

            cts.Dispose();
        }

        private void AssertCacheLeaseEntry(ICacheEntry entry, 
            IDataJobNode payloadCarrierRunner, bool isRoot, 
            EntryStatus status, bool isFifo,Guid jobRootId, 
            Guid parentJobId,
            string? payloadJson,
            string nameJson,
            bool entryDeleted)
        {
            Assert.That(entry.JobId, Is.EqualTo(payloadCarrierRunner.Id));
            Assert.That(entry.JobRootId, Is.EqualTo(jobRootId));
            Assert.That(entry.IsRoot, Is.EqualTo(isRoot));
            Assert.That(entry.Status, Is.EqualTo(status));
            Assert.That(entry.IsFifo, Is.EqualTo(isFifo));
            Assert.That(entry.JobNodeRecordDto.IsRoot, Is.EqualTo(isRoot));
            Assert.That(entry.JobNodeRecordDto.ParentJobId, Is.EqualTo(parentJobId));
            Assert.That(entry.JobNodeRecordDto.JobId, Is.EqualTo(payloadCarrierRunner.Id));
            Assert.That(entry.JobNodeRecordDto.Name, Is.EqualTo(payloadCarrierRunner.Name));
            if (payloadJson != null)
            {
                Assert.That(entry.JobNodeRecordDto.PayloadJson, Is.EqualTo(payloadJson));
            }
            Assert.That(entry.Deleted, Is.EqualTo(entryDeleted));
        }

        private string SerializePayload(IPayload payload)
        {
            return _universalPayloadSerializer.Serialize(payload, payload.GetType());
        }

        private ICacheEntry WaitForEntryStatus(Guid jobId, EntryStatus status, int timeoutMs = 3000)
        {
            var elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                var entry = _memCache.GetByJobId(jobId);
                if (entry != null && entry.Status == status)
                {
                    return entry;
                }

                Task.Delay(25).Wait();
                elapsedMs += 25;
            }

            Assert.Fail($"Entry {jobId} did not reach status {status} within {timeoutMs} ms.");
            return _memCache.GetByJobId(jobId)!;
        }
    }
}

