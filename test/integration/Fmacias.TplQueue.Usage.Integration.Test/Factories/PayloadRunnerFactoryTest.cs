using Fmacias.TplQueue.Cache.Abstract.Factories;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Core.Jobs.Internals;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Integration.Test.Cache;
using Fmacias.TplQueue.RetryPolicies;
using System.Text.Json.Serialization;
using static Fmacias.TplQueue.Integration.Test.Cache.CacheFactoryTests;

namespace Fmacias.TplQueue.Integration.Test.Factories
{
    [TestFixture]
    public class PayloadJobFactoryTests
    {
        private IDataJobFactory _payloadJobFactory = null!;
        private IUniversalDataSerializer _universalPayloadSerializer = null!;
        private Dictionary<string, IRetryPolicyOptions> _retryPolicyOptions = null!;
        private readonly List<string> _recordingExecutions = new List<string>();
        private IPayloadHandlers _jobHandlerResolver = null!;
        private IRetryPolicyAbstractFactory _retryPolicyGenericFactory = null!;

        [SetUp]
        public void SetUp()
        {
            _recordingExecutions.Clear();
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {

            _retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var fakePayloadRegistration = new Helper.HandlerRegistration(
                new FakePayload("Fakepayload_1"),
                DelegatePayloadHandler.Create((payloadObject, ct) =>
                {
                    return Task.CompletedTask;
                }));
            var recordingPayloadRegistration = new Helper.HandlerRegistration(
                new RecordingPayload("Recording-Name"),
                DelegatePayloadHandler.Create((payloadObject, ct) =>
                {
                    var typed = (RecordingPayload)payloadObject;
                    lock (_recordingExecutions)
                    {
                        _recordingExecutions.Add(typed.Name);
                    }
                    return Task.CompletedTask;
                }));
            _jobHandlerResolver = Helper.CreateHandlerResolver(fakePayloadRegistration, recordingPayloadRegistration);
            var api = Helper.GetApi(
                _retryPolicyOptions,
                new Dictionary<string, IQOptions>(),
                fakePayloadRegistration,
                recordingPayloadRegistration);
            _retryPolicyGenericFactory = api.RetryPolicyAbstractFactory;
            _payloadJobFactory = api.DataJobFactory;
            _universalPayloadSerializer = Helper.CreatePayloadSerializer();
        }

        [Test]
        public void Create_WithPayload_ReturnsPayloadJob()
        {
            var payload = new FakePayload("FakePayload2");
            var job = _payloadJobFactory.DataJobRoot(
                payload, 
                DelegatePayloadHandler.Create((o,ct)=> Task.CompletedTask), 
                "job-name",
                () => NoRetryPolicy.Create());
            Assert.That(job, Is.Not.Null);
            Assert.That(job.Payload, Is.SameAs(payload));
            Assert.That(job.Name, Is.EqualTo("job-name"));
            Assert.That(job.PayloadType, Is.EqualTo(typeof(FakePayload)));
        }

        [Test]
        public void CreateRoot_WithPayload_ReturnsPayloadJobRoot()
        {
            var payload = new FakePayload("FakePayload3");
            var root = _payloadJobFactory.DataJobRoot(
                payload,
                DelegatePayloadHandler.Create((o, ct) => Task.CompletedTask),
                name: "root-job",
                () => NoRetryPolicy.Create());
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Payload, Is.SameAs(payload));
            Assert.That(root.Name, Is.EqualTo("root-job"));
        }

        [Test]
        public void CreatePayloadJob_ByJobNodeDto_Test()
        {
            var payload = new FakePayload("FakePayload_4");
            var registration = new Helper.HandlerRegistration(
                payload,
                DelegatePayloadHandler.Create((payloadObject, ct) =>
                {
                    return Task.CompletedTask;
                })
            );
            var jobHandlerResolver = Helper.CreateHandlerResolver(registration);
            var api = Helper.GetApi(
                _retryPolicyOptions,
                new Dictionary<string, IQOptions>(),
                registration);
            var memCache = api
                .Cache<IMemCache>(
                    MemCacheFactory.Create(),
                    _universalPayloadSerializer,
                    new TestTypeResolver());

            var payloadRoot = _payloadJobFactory.DataJobRoot(
                payload,
                DelegatePayloadHandler.Create((o,ct) => Task.CompletedTask),
                name: "root-job",
                () => NoRetryPolicy.Create());
            
            var payloadChild = _payloadJobFactory.DataJob(
                payload,
                DelegatePayloadHandler.Create((o, ct) => Task.CompletedTask),
                name: "rehydrated");

            payloadRoot.After(payloadChild);
            var traversedDtoJobs = memCache.Dehydrate(payloadRoot, isFifo: false);
            Assert.That(payloadRoot.Id, Is.EqualTo(traversedDtoJobs.Last().JobId));

            Assert.That(memCache.TryHydrateNextJob(out var hidratedPayloadJobRoot, out var cacheEntry), Is.True);
            Assert.That(hidratedPayloadJobRoot.Id, Is.EqualTo(traversedDtoJobs.Last().JobId));
            Assert.That(hidratedPayloadJobRoot.Id, Is.EqualTo(payloadRoot.Id));
            Assert.That(hidratedPayloadJobRoot.GetDependentDataJobs().Select(c => c.Id), Does.Contain(payloadChild.Id));


            var childLeaseEntry = memCache.GetByJobId(payloadChild.Id);
            Assert.That(childLeaseEntry, Is.Not.Null, "Child entry should be present in cache after append.");
            var payloadInstance = childLeaseEntry.JobNodeRecordDto.Deserialize<FakePayload>(_universalPayloadSerializer);
            var payloadDataJob = _payloadJobFactory.DataJob(
                payloadInstance,
                jobHandlerResolver.Handler(payloadInstance.PayloadId),
                childLeaseEntry.JobNodeRecordDto.Name
            );
            Assert.That(payloadDataJob, Is.Not.Null);
            Assert.That(payloadDataJob.PayloadType, Is.EqualTo(typeof(FakePayload)));
            Assert.That(payloadDataJob.GetPayload(), Is.InstanceOf<FakePayload>());
            Assert.That(payloadDataJob.Name, Is.EqualTo(childLeaseEntry.JobNodeRecordDto.Name));
        }

        /// <summary>
        /// Type <see cref="IJobCommandAsync"/> is internal to core, to avoid
        /// execution of runners out from TplCore.Context.
        /// 
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task GetPayloadJobFactory_ProducesExecutableRunners()
        {
            //Arrange
            var payload = new RecordingPayload("Recording-Dummy-Name");
            var registration = new Helper.HandlerRegistration(
                payload,
                DelegatePayloadHandler.Create((payloadObject, ct) => {
                    var typed = (RecordingPayload)payloadObject;
                    lock (_recordingExecutions)
                    {
                        _recordingExecutions.Add(typed.Name);
                    }
                    return Task.CompletedTask;

                }));
            var handlerResolver = Helper.CreateHandlerResolver(registration);
            var api = Helper.GetApi(
                _retryPolicyOptions,
                new Dictionary<string, IQOptions>(),
                registration);
            var dataJobFactory = api.DataJobFactory;
            var payloadJobRoot = dataJobFactory.DataJobRoot(
                payload, 
                handlerResolver.Handler(payload.PayloadId), 
                "payload",
                () => _retryPolicyGenericFactory.PolicyByName("none", _retryPolicyOptions)
            );
            var payloadAsAdapter = (IJobAdapter)payloadJobRoot;
            Assert.IsInstanceOf<IJobAdapter>(payloadJobRoot);

            using var queueOrkestator = CoreApi.Create().QFactory.Parallel(
                Guid.NewGuid(),
                "ParallelQueue",
                maxParallelism: 1,
                logger: Helper.GetLogger<IParallelQ>(),
                retryPolicyFactory: () => NoRetryPolicy.Create());

            queueOrkestator.ResumePolling();
            queueOrkestator.Enqueue(payloadJobRoot, CancellationToken.None);

            await payloadJobRoot.WaitUntilFinishedAsync();
            lock (_recordingExecutions)
            {
                Assert.That(_recordingExecutions, Does.Contain(payload.Name));
            }
            Assert.IsInstanceOf<IJobAdapter>(payloadJobRoot);

        }

        [Test]
        public void PayloadJob_CopyInfo_SerializesPayloadSnapshot()
        {
            var payload = new RecordingPayload("payload-run");
            var registration = new Helper.HandlerRegistration(
                payload,
                DelegatePayloadHandler.Create((payload, ct) => Task.CompletedTask)
            );
            var handlerResolver = Helper.CreateHandlerResolver(registration);
            var api = Helper.GetApi(
                _retryPolicyOptions,
                new Dictionary<string, IQOptions>(),
                registration);
            var dataJobFactory = api.DataJobFactory;
            var payloadChildJob = dataJobFactory.DataJob<RecordingPayload>(
                payload,
                handlerResolver.Handler(payload.PayloadId),
                "payload-child"
            );
            var info = payloadChildJob.CopyInfo();
            Assert.That(info, Is.Not.Null);
            Assert.That(payloadChildJob.Payload, Is.SameAs(payload));
        }
    }
    internal sealed class FakePayload : IPayload
    {
        public FakePayload(string payloadId)
        {
            PayloadId = payloadId;
        }

        public FakePayload()
        {
            PayloadId = Guid.NewGuid().ToString();
        }

        public string PayloadId { get; init; }
        [JsonIgnore]
        public int X { get; init; }
        public DateTime CollectionTime => DateTime.UtcNow;
    }
}
