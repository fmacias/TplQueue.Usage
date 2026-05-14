using Fmacias.TplQueue.Cache.Abstract.Factories;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using System.Text.Json.Serialization;

namespace Fmacias.TplQueue.Integration.Test.Cache
{
    public class RecordingPayload : IPayload
    {
        private readonly DateTime _collectionTime;
        public string Name { get; init; }
        public string PayloadId => "recording";
        public bool Executed { get; init; }
        public DateTime CollectionTime => _collectionTime;

        [JsonConstructor]
        public RecordingPayload(string name)
        {
            Name = name;
            _collectionTime = DateTime.UtcNow;
        }
    }

    [TestFixture]
    public class CacheFactoryTests
    {
        private IDataJobFactory _dataJobFactory = null!;
        private IUniversalDataSerializer _serializer = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
        private IApi _api = null!;
        private Dictionary<string, IRetryPolicyOptions> _retryPolicyOptions = null!;
        private readonly List<string> _executions = new List<string>();
        private IMemCache _memCache = null!;
        private IPayloadHandlers _payloadHandlerResolver = null!;
        [SetUp]
        public void SetUp()
        {
            _executions.Clear();
            _retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var registration = new Helper.HandlerRegistration(
                new RecordingPayload("recodingPayload"),
                DelegatePayloadHandler.Create((payload, ct) =>
                {
                    var typed = (RecordingPayload)payload;
                    lock (_executions)
                    {
                        _executions.Add(typed.Name);
                    }
                    return Task.CompletedTask;
                }));
            _payloadHandlerResolver = Helper.CreateHandlerResolver(registration);
            _api = Helper.GetApi(
                _retryPolicyOptions,
                new Dictionary<string, IQOptions>(),
                registration);
            _retryPolicyFactory = _api.RetryPolicyAbstractFactory;
            _dataJobFactory = _api.DataJobFactory;
            _serializer = _api.SystemTexSerializerFactory().Serializer();
        }

        [Test]
        public void CreateMemCache_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MemCacheFactory.Create().CreateCache(
                    null!,
                    _dataJobFactory,
                    new TestTypeResolver(), _payloadHandlerResolver, _retryPolicyFactory));
            
            Assert.Throws<ArgumentNullException>(() => MemCacheFactory.Create().CreateCache(
                _serializer,
                null!,
                new TestTypeResolver(), _payloadHandlerResolver, _retryPolicyFactory));

            Assert.Throws<ArgumentNullException>(() => MemCacheFactory.Create().CreateCache(
                _serializer,
                _dataJobFactory,
                null!, _payloadHandlerResolver, _retryPolicyFactory));

            Assert.IsInstanceOf<IMemCache>(MemCacheFactory.Create().CreateCache(
                _serializer, _dataJobFactory, new TestTypeResolver(), _payloadHandlerResolver, _retryPolicyFactory));
        }

        [Test]
        public async Task CreateMemCache_LeasesGraphWithDependencies()
        {
            var memCache = MemCacheFactory.Create().CreateCache(
                _serializer,
                _dataJobFactory,
                RuntimeNodeTypeResolverFactory.Create().Resolver(),
                _payloadHandlerResolver,
                _retryPolicyFactory
            );

            var child = _dataJobFactory
                .DataJob<RecordingPayload>(
                    new RecordingPayload("child"),
                    DelegatePayloadHandler.Create((payloadObject, ct) =>
                    {
                        return Task.CompletedTask;
                    })
                );

            var rootPayload = new RecordingPayload("root");
            var root = _dataJobFactory.DataJobRoot(
                rootPayload,
                _payloadHandlerResolver.Handler(rootPayload.PayloadId),
                "root",
                () => _retryPolicyFactory.PolicyByName("none", _retryPolicyOptions)
            );
            
            root.After(child);

            var jobDtoNodes = memCache.Dehydrate(root, isFifo: true);
            Assert.That(jobDtoNodes.Count, Is.EqualTo(2));
            Assert.That(memCache.TryHydrateNextJob(out var leasedDataJobRoot, out var lease), Is.True);
            Assert.That(lease.Status, Is.EqualTo(EntryStatus.Pending));
            Assert.That(leasedDataJobRoot.Dependencies.Count(), Is.EqualTo(1));
            
            var rootPayloadObject = leasedDataJobRoot.GetPayload();
            Assert.IsInstanceOf<RecordingPayload>(rootPayloadObject);
            Assert.That(((RecordingPayload)rootPayloadObject).Name, Is.EqualTo("root"));

            var childPayloadObject = leasedDataJobRoot.GetDependentDataJobs()[0].GetPayload();
            Assert.IsInstanceOf<RecordingPayload>(childPayloadObject);
            Assert.That(((RecordingPayload)childPayloadObject).Name, Is.EqualTo("child"));
        }
        public sealed class TestTypeResolver : ITypeResolver
        {
            /// <inheritdoc />
            public Type Resolve(string payloadTypeName)
            {
                if (string.IsNullOrWhiteSpace(payloadTypeName))
                    throw new ArgumentException("Payload type name cannot be null/empty.", nameof(payloadTypeName));

                var type = Type.GetType(payloadTypeName, throwOnError: false);
                if (type is null)
                    throw new InvalidOperationException($"Cannot resolve CLR type from '{payloadTypeName}'.");

                return type;
            }
        }
    }
}
