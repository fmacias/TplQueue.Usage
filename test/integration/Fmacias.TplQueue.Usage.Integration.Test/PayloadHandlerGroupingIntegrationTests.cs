using System.Collections.Concurrent;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;

namespace Fmacias.TplQueue.Integration.Test
{
    [TestFixture]
    public sealed class PayloadHandlerGroupingIntegrationTests
    {
        [Test]
        public async Task CacheHydration_UsesHandlerRegisteredThroughApplicationGrouping()
        {
            var executions = new ConcurrentQueue<string>();
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var api = Helper.GetApi(
                retryPolicyOptions,
                new Dictionary<string, IQOptions>());
            RecordingPayloadRegistration.RegisterOn(api, executions);
            var payload = new RecordingPayload("plugin-execution");
            var root = api.DataJobFactory.DataJobRoot(
                payload,
                new FailingOriginalHandler(),
                "plugin-root",
                () => NoRetryPolicy.Create());
            var cache = api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                api.SystemTextSerializerFactory().Serializer(),
                new IntegrationTypeResolver());

            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var hydratedRoot, out var lease), Is.True);
            Assert.That(lease.JobId, Is.EqualTo(root.Id));

            using var queue = api.QFactory.Parallel(
                Guid.NewGuid(),
                "plugin-payload-queue",
                maxParallelism: 1,
                logger: Helper.GetLogger<IParallelQ>(),
                retryPolicyFactory: () => NoRetryPolicy.Create());
            hydratedRoot.Enqueue(queue, CancellationToken.None);
            queue.ResumePolling();

            await hydratedRoot.WaitUntilFinishedAsync();

            CollectionAssert.AreEqual(new[] { "plugin-execution" }, executions.ToArray());
        }

        private static class RecordingPayloadRegistration
        {
            public static void RegisterOn(IApi api, ConcurrentQueue<string> executions)
            {
                if (api == null) throw new ArgumentNullException(nameof(api));
                if (executions == null) throw new ArgumentNullException(nameof(executions));

                api.RegisterPayloadHandler<RecordingPayload>(
                    RecordingPayload.PayloadHandlerKey,
                    (payload, ct) =>
                    {
                        executions.Enqueue(payload.Label);
                        return Task.CompletedTask;
                    });
            }
        }

        private sealed class FailingOriginalHandler : IHandler
        {
            public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException(
                    "The original dehydration handler should not be used after cache hydration.");
            }
        }

        private sealed class RecordingPayload : IPayload
        {
            public const string PayloadHandlerKey = "test/integration/recording-v1";

            public RecordingPayload(string label)
            {
                Label = label;
            }

            public string Label { get; }
            public string PayloadId => PayloadHandlerKey;
            public DateTime CollectionTime => DateTime.UtcNow;
        }

        private sealed class IntegrationTypeResolver : ITypeResolver
        {
            public Type Resolve(string payloadTypeName)
            {
                if (string.IsNullOrWhiteSpace(payloadTypeName))
                    throw new ArgumentException("Payload type name cannot be null or empty.", nameof(payloadTypeName));

                var type = Type.GetType(payloadTypeName, throwOnError: false);
                if (type == null)
                    throw new InvalidOperationException($"Cannot resolve CLR type from '{payloadTypeName}'.");

                return type;
            }
        }
    }
}
