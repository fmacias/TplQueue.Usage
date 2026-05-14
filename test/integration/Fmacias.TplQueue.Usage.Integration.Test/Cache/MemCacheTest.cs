using Fmacias.TplQueue.RetryPolicies;
using Fmacias.TplQueue.Exceptions;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Cache.MemCache;
using static Fmacias.TplQueue.Integration.Test.Cache.CacheFactoryTests;
using System.Runtime.CompilerServices;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Core.Jobs;

namespace Fmacias.TplQueue.Integration.Test.Cache
{
    [TestFixture]
    public class MemCacheTests
    {
        private IApi _api = null!;
        private IDataJobFactory _dataJobFactory = null!;
        private IUniversalDataSerializer _universalPayloadSerializer = null!;
        private IPayloadHandlers _payloadHandlerResolver = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var queueOptions = new Dictionary<string, IQOptions>();
            var registration = new Helper.HandlerRegistration(
                new DummyPayload(),
                DelegatePayloadHandler.Create((payload, ct) => Task.CompletedTask));
            _payloadHandlerResolver = Helper.CreateHandlerResolver(registration);
            _api = Helper.GetApi(
                retryPolicyOptions,
                queueOptions,
                registration);
            _dataJobFactory = _api.DataJobFactory;
            _universalPayloadSerializer = _api.SystemTexSerializerFactory().Serializer();
        }

        private class DummyPayload : IPayload
        {
            public string PayloadId => "dummy";

            public DateTime CollectionTime => DateTime.UtcNow;
        }

        [Test]
        public void Append_CreatesEntries_InLeasedState()
        {
            var cache = CreateCache();
            IDataJobRoot<DummyPayload> root = CreateRoot();
            IDataJob<DummyPayload> child = CreateChild("Child-1");

            child.Then(root);
            //root.After(child);
            
            var list = cache.Dehydrate(root, isFifo: true);

            Assert.That(list.Count,  Is.EqualTo(2));
            var ok = cache.TryHydrateNextJob(out var payloadCarrierRoot, out var rootLease);
            cache.LeaseRootNode(rootLease);
            Assert.That(ok, Is.True);
            Assert.That(rootLease.Status, Is.EqualTo(EntryStatus.Leased));
            Assert.That(rootLease.IsFifo, Is.True);
            Assert.That(rootLease.JobRootId, Is.EqualTo(root.Id));

            Assert.That(root.Id, Is.EqualTo(payloadCarrierRoot.Id));
            Assert.That(payloadCarrierRoot.Name, Is.EqualTo(root.Name));
            var root1rp = root.GetRetryPolicyFactory()();
            var root2rp = payloadCarrierRoot.GetRetryPolicyFactory()();
            
            Assert.IsInstanceOf<ILinearBackoff>(root2rp);

            var childEntry = cache.GetByJobId(child.Id);
            Assert.That(childEntry.Status, Is.EqualTo(EntryStatus.Leased));
            Assert.That(childEntry.IsFifo, Is.False);
            Assert.That(childEntry.JobRootId, Is.EqualTo(root.Id));
            Assert.That(childEntry.JobNodeRecordDto.Name, Is.EqualTo(child.Name));

        }
        [Test]
        public void Append_ConcatenatedEntries_Test()
        {
            var memCache = CreateCache();
            var root = CreateRoot();
            var child = CreateChild("child-job");
            var grandChild = CreateChild("grant-child");

            var addedChild = grandChild.Then(child);
            Assert.That(addedChild.Id, Is.EqualTo(child.Id));

            child.Then(root);

            var entries = memCache.Dehydrate(root, isFifo: true);
            Assert.That(entries.Count, Is.EqualTo(3));

            var rootEntry = memCache.GetByJobId(root.Id);
            Assert.AreEqual(rootEntry.IsRoot, true);
            Assert.AreEqual(rootEntry.IsFifo, true);
            Assert.AreEqual(rootEntry.Status, EntryStatus.Pending);
            Assert.AreEqual(rootEntry.JobId, root.Id);
            Assert.AreEqual(rootEntry.JobRootId, root.Id);
            Assert.AreEqual(rootEntry.ParentJobId, Guid.Empty);
            Assert.AreEqual(rootEntry.JobNodeRecordDto.JobId, root.Id);
            Assert.AreEqual(rootEntry.JobNodeRecordDto.ParentJobId, Guid.Empty);
            Assert.AreEqual(rootEntry.JobNodeRecordDto.Name, root.Name);

            var childEntry = memCache.GetByJobId(child.Id);
            Assert.AreEqual(childEntry.IsRoot, false);
            Assert.AreEqual(childEntry.IsFifo, false, "Only root can be fifo");
            Assert.AreEqual(childEntry.Status, EntryStatus.Pending);
            Assert.AreEqual(childEntry.JobId, child.Id);
            Assert.AreEqual(childEntry.JobRootId, root.Id);
            Assert.AreEqual(childEntry.ParentJobId, root.Id);
            Assert.AreEqual(childEntry.JobNodeRecordDto.JobId, child.Id);
            Assert.AreEqual(childEntry.JobNodeRecordDto.ParentJobId, root.Id);
            Assert.AreEqual(childEntry.JobNodeRecordDto.Name, child.Name);

            var grandChildEntry = memCache.GetByJobId(grandChild.Id);
            Assert.AreEqual(grandChildEntry.IsRoot, false);
            Assert.AreEqual(grandChildEntry.IsFifo, false, "Only root can be fifo");
            Assert.AreEqual(grandChildEntry.Status, EntryStatus.Pending);
            Assert.AreEqual(grandChildEntry.JobId, grandChild.Id);
            Assert.AreEqual(grandChildEntry.JobRootId, root.Id);
            Assert.AreEqual(grandChildEntry.ParentJobId, child.Id);
            Assert.AreEqual(grandChildEntry.JobNodeRecordDto.JobId, grandChild.Id);
            Assert.AreEqual(grandChildEntry.JobNodeRecordDto.ParentJobId, child.Id);
            Assert.AreEqual(grandChildEntry.JobNodeRecordDto.Name, grandChild.Name);

        }

        private IDataJobRoot<DummyPayload> CreateRoot()
        {
            var payload = new DummyPayload();
            var linearBackoffFactory = LinearBackoffFactory.Create();
            var root = _dataJobFactory 
                .DataJobRoot(
                    payload,
                    _payloadHandlerResolver.Handler(payload.PayloadId),
                    name: "root-job",
                    retryPolicy: () => linearBackoffFactory.CreatePolicy());
            return root;
        }

        private IDataJob<DummyPayload> CreateChild(string name)
        {
            var payload = new DummyPayload();

            var child = _dataJobFactory.DataJob(
                payload,
                _payloadHandlerResolver.Handler(payload.PayloadId),
                name);
            return child;
        }


        private IMemCache CreateCache()
        {
            return _api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                _universalPayloadSerializer,
                new TestTypeResolver());
        }

        [Test]
        public void AckNode_MarksAllLeasesForRunner_AsAck()
        {
            //Arrange
            var memCache = CreateCache();
            var dataJobRoot = CreateRoot(); 
            var dtoNodes = memCache.Dehydrate(dataJobRoot, isFifo: false);

            //Act & Assert
            var hidrated = memCache.TryHydrateNextJob(out var payloadDataJobRoot, out var cacheEntry);
            Assert.AreNotSame(dataJobRoot, payloadDataJobRoot);
            Assert.That(hidrated, Is.True);

            //Act & Assert
            memCache.AckNode(cacheEntry.JobId, payloadDataJobRoot);
            Assert.That(dataJobRoot.Id, Is.EqualTo(payloadDataJobRoot.Id));
            
            //Act & Assert
            var deleted = memCache.DeleteRootNode(dataJobRoot.Id);
            Assert.That(deleted, Is.EqualTo(true));
            Assert.That(memCache.TryHydrateNextJob(out var a, out var b), Is.False);
        }

        [Test]
        public void FailChildNode_MarksFailed_RottNotFinalized()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            var child = CreateChild("Child-Job");
            root.After(child);

            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var payloadRoot, out var lease), Is.True);

            cache.FailNode(child.Id, "boom");
            Assert.That(cache.DeleteRootNode(root.Id), Is.EqualTo(false));
        }
        [Test]
        public void RootChildFinalizedChildNot_Validation()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            var child = CreateChild("Child-Job");
            root.After(child);

            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var payloadRoot, out var lease), Is.True);

            cache.AckNode(root.Id, payloadRoot);
            
            Assert.Throws<TplQueueErrorException>(() => cache.DeleteRootNode(root.Id));
        }

        [Test]
        public void FailRootNode_MarksFailed_AndPreventsValidation()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            Assert.IsInstanceOf<IJobRoot>(root);
            var child = CreateChild("Child-job");
            Assert.IsInstanceOf<IJob>(child);
            root.After(child);

            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var payloadRoot, out var lease), Is.True);
            cache.AckNode(child.Id, payloadRoot);
            cache.FailNode(root.Id, "");
            
            Assert.That(
                cache.GetByJobId(child.Id).Status,
                Is.EqualTo(EntryStatus.Acknownledged));
            Assert.That(
                cache.GetByJobId(root.Id).Status,
                Is.EqualTo(EntryStatus.Failed));
            Assert.That(cache.DeleteRootNode(root.Id), Is.EqualTo(true));
        }
        [Test]
        public void CancelNode_MarksCanceled_AndAllowsTerminal()
        {
            var cache = CreateCache();
            var root = CreateRoot();

            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var payloadRoot,out var lease), Is.True);

            cache.CancelNode(lease.JobId);
            Assert.That(cache.DeleteRootNode(root.Id), Is.EqualTo(true));

        }

        [Test]
        public void TryExtractPayloadCarrierRoot_RehydratesGraph()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var payloadRoot, out var lease), Is.True);
            Assert.That(payloadRoot, Is.Not.Null);
            Assert.That(payloadRoot.Id, Is.EqualTo(root.Id));
        }

        [Test]
        public void TryLeaseNextRoot_RehydratesDependenciesGraph()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            var child = CreateChild("child-job");
            var grandChild = CreateChild("grant-child");

            child.After(grandChild);
            root.After(child);

            var entries = cache.Dehydrate(root, isFifo: false);
            
            Assert.That(cache.TryHydrateNextJob(out var payloadRoot, out _), Is.True);
            var dependencies = payloadRoot.GetDependentDataJobs().ToArray();

            Assert.That(dependencies.Length, Is.EqualTo(1));
            Assert.That(dependencies[0].Id, Is.EqualTo(child.Id));
            Assert.That(
                dependencies[0].GetDependentDataJobs().Select(p => p.Id),
                Does.Contain(grandChild.Id));
        }

        [Test]
        public void CleanFinalizedTest()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            var child = CreateChild("child-job");

            root.After(child);
            cache.Dehydrate(root, isFifo: false);

            cache.FailNode(child.Id, "boom");

            cache.CleanFinalized();
            Assert.That(cache.GetByJobId(child.Id), Is.EqualTo(null));
        }
        [Test]
        public void RootFinalizedTest()
        {
            var cache = CreateCache();
            var root = CreateRoot();
            var child = CreateChild("child-job");

            root.After(child);
            cache.Dehydrate(root, isFifo: false);

            cache.FailNode(child.Id, "boom");
            cache.FailNode(root.Id,"root also failes");
            Assert.That(cache.DeleteRootNode(root.Id), Is.EqualTo(true));
            cache.SuccessRootNode(root.Id);
            Assert.AreEqual(true, cache.GetByJobId(child.Id).Deleted);
            Assert.AreEqual(true, cache.GetByJobId(root.Id).Deleted);
        }
    }
}
