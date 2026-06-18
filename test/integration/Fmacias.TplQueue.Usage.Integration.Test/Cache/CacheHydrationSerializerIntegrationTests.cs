using Fmacias.TplQueue.Cache.Abstract.Factories;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using System.Collections.Generic;

namespace Fmacias.TplQueue.Integration.Test.Cache
{
    [TestFixture]
    [Category("TPLQ-030")]
    public sealed class CacheHydrationSerializerIntegrationTests
    {
        private const string JsonSerializerName = "JSON";
        private const string XmlSerializerName = "XML";

        [TestCase(JsonSerializerName)]
        [TestCase(XmlSerializerName)]
        public void TryHydrateNextJob_WithSupportedSerializer_HydratesRootAndChild(string serializerName)
        {
            var rootPayload = IntegrationPayload.Create(
                payloadId: $"integration/{serializerName.ToLowerInvariant()}/root/v1",
                name: "root",
                value: 10);
            var childPayload = IntegrationPayload.Create(
                payloadId: $"integration/{serializerName.ToLowerInvariant()}/child/v1",
                name: "child",
                value: 20);
            var handler = DelegatePayloadHandler.Create((payload, ct) => Task.CompletedTask);
            var api = Helper.GetApi(
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>(),
                new Helper.HandlerRegistration(rootPayload, handler),
                new Helper.HandlerRegistration(childPayload, handler));
            var root = api.DataJobFactory.DataJobRoot(rootPayload, handler, "root");
            var child = api.DataJobFactory.DataJob(childPayload, handler, "child");
            root.After(child);

            var typeResolver = new RecordingTypeResolver(RuntimeNodeTypeResolverFactory.Create().Resolver());
            var cache = api.Cache(
                MemCacheFactory.Create(),
                CreateSerializer(api, serializerName),
                typeResolver);

            var dehydratedNodes = cache.Dehydrate(root, isFifo: false);
            var hydrated = cache.TryHydrateNextJob(out var hydratedRoot, out var lease);

            var rootEntry = cache.GetByJobId(root.Id);
            var childEntry = cache.GetByJobId(child.Id);
            var hydratedRootPayload = (IntegrationPayload)hydratedRoot.GetPayload();
            var hydratedChild = hydratedRoot.GetDependentDataJobs().Single();
            var hydratedChildPayload = (IntegrationPayload)hydratedChild.GetPayload();

            Assert.Multiple(() =>
            {
                Assert.That(dehydratedNodes, Has.Count.EqualTo(2));
                Assert.That(hydrated, Is.True);
                Assert.That(lease, Is.SameAs(rootEntry));
                Assert.That(rootEntry.JobNodeRecordDto.PayloadTypeName, Is.EqualTo(typeof(IntegrationPayload).AssemblyQualifiedName));
                Assert.That(childEntry.JobNodeRecordDto.PayloadTypeName, Is.EqualTo(typeof(IntegrationPayload).AssemblyQualifiedName));
                Assert.That(rootEntry.JobNodeRecordDto.PayloadHandlerKey, Is.EqualTo(rootPayload.PayloadId));
                Assert.That(childEntry.JobNodeRecordDto.PayloadHandlerKey, Is.EqualTo(childPayload.PayloadId));
                Assert.That(typeResolver.ResolvedPayloadTypeNames, Has.Exactly(2).EqualTo(typeof(IntegrationPayload).AssemblyQualifiedName));
                PayloadSerializationAssert.MatchesSerializer(serializerName, rootEntry.JobNodeRecordDto.SerializedPayload);
                PayloadSerializationAssert.MatchesSerializer(serializerName, childEntry.JobNodeRecordDto.SerializedPayload);
                Assert.That(hydratedRootPayload.PayloadId, Is.EqualTo(rootPayload.PayloadId));
                Assert.That(hydratedRootPayload.Name, Is.EqualTo(rootPayload.Name));
                Assert.That(hydratedRootPayload.Value, Is.EqualTo(rootPayload.Value));
                Assert.That(hydratedChildPayload.PayloadId, Is.EqualTo(childPayload.PayloadId));
                Assert.That(hydratedChildPayload.Name, Is.EqualTo(childPayload.Name));
                Assert.That(hydratedChildPayload.Value, Is.EqualTo(childPayload.Value));
            });
        }

        [Test]
        public void TryHydrateNextJob_WhenPayloadHandlerIsNotRegistered_ThrowsKeyNotFoundException()
        {
            var payload = IntegrationPayload.Create(
                payloadId: "integration/json/missing-handler/v1",
                name: "root",
                value: 30);
            var handler = DelegatePayloadHandler.Create((payloadObject, ct) => Task.CompletedTask);
            var api = Helper.GetApi(
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());
            var root = api.DataJobFactory.DataJobRoot(payload, handler, "root");
            var cache = api.Cache(
                MemCacheFactory.Create(),
                api.SystemTextSerializerFactory().Serializer(),
                RuntimeNodeTypeResolverFactory.Create().Resolver());

            cache.Dehydrate(root, isFifo: false);

            var exception = Assert.Throws<KeyNotFoundException>(() =>
                cache.TryHydrateNextJob(out _, out _));
            Assert.That(exception!.Message, Does.Contain(payload.PayloadId));
        }

        private static IUniversalDataSerializer CreateSerializer(IApi api, string serializerName)
        {
            if (serializerName == JsonSerializerName)
            {
                return api.SystemTextSerializerFactory().Serializer();
            }

            if (serializerName == XmlSerializerName)
            {
                return api.XmlSerializerFactory().Serializer();
            }

            throw new ArgumentOutOfRangeException(nameof(serializerName), serializerName, "Unsupported serializer.");
        }

        private static class PayloadSerializationAssert
        {
            public static void MatchesSerializer(string serializerName, string serializedPayload)
            {
                if (serializerName == JsonSerializerName)
                {
                    Assert.That(serializedPayload, Does.Contain("\"PayloadId\""));
                    return;
                }

                Assert.That(serializedPayload, Does.Contain("<PayloadId>"));
            }
        }

        public sealed class IntegrationPayload : IPayload
        {
            public IntegrationPayload()
            {
                PayloadId = string.Empty;
                Name = string.Empty;
                CollectionTime = DateTime.MinValue;
            }

            private IntegrationPayload(string payloadId, string name, int value)
            {
                PayloadId = payloadId;
                Name = name;
                Value = value;
                CollectionTime = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
            }

            public string PayloadId { get; set; }
            public string Name { get; set; }
            public int Value { get; set; }
            public DateTime CollectionTime { get; set; }

            public static IntegrationPayload Create(string payloadId, string name, int value)
            {
                return new IntegrationPayload(payloadId, name, value);
            }
        }

        private sealed class RecordingTypeResolver : ITypeResolver
        {
            private readonly ITypeResolver _inner;
            private readonly List<string> _resolvedPayloadTypeNames = new List<string>();

            public RecordingTypeResolver(ITypeResolver inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public IReadOnlyCollection<string> ResolvedPayloadTypeNames => _resolvedPayloadTypeNames;

            public Type Resolve(string payloadTypeName)
            {
                _resolvedPayloadTypeNames.Add(payloadTypeName);
                return _inner.Resolve(payloadTypeName);
            }
        }
    }
}
