using System.Collections.Concurrent;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;

namespace Fmacias.TplQueue.Integration.Test
{
    [TestFixture]
    public sealed class SystemTextJsonSerializerIntegrationTests
    {
        [Test]
        public async Task SystemTextJsonSerializer_RoundTripsPayloadThroughCacheHydrationAndQueueExecution()
        {
            var executions = new ConcurrentQueue<string>();
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var api = Helper.GetApi(
                retryPolicyOptions,
                new Dictionary<string, IQOptions>());
            api.RegisterPayloadHandler<JsonIntegrationPayload>(
                JsonIntegrationPayload.PayloadHandlerKey,
                (payload, ct) =>
                {
                    executions.Enqueue($"{payload.Label}:{payload.Sequence}");
                    return Task.CompletedTask;
                });
            var payload = new JsonIntegrationPayload
            {
                Label = "json-cache",
                Sequence = 7
            };
            var root = api.DataJobFactory.DataJobRoot(
                payload,
                new FailingOriginalHandler(),
                "json-serializer-root",
                () => NoRetryPolicy.Create());
            var cache = api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                api.SystemTextSerializerFactory().Serializer());

            var dehydratedNodes = cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var hydratedRoot, out var lease), Is.True);
            var hydratedPayload = (JsonIntegrationPayload)hydratedRoot.GetPayload();

            using var queue = api.QFactory.Parallel(
                Guid.NewGuid(),
                "json-serializer-payload-queue",
                maxParallelism: 1,
                logger: Helper.GetLogger<IParallelQ>(),
                retryPolicyFactory: () => NoRetryPolicy.Create());
            hydratedRoot.Enqueue(queue, CancellationToken.None);
            queue.ResumePolling();

            await hydratedRoot.WaitUntilFinishedAsync();

            Assert.Multiple(() =>
            {
                Assert.That(dehydratedNodes, Has.Count.EqualTo(1));
                Assert.That(lease.JobId, Is.EqualTo(root.Id));
                Assert.That(hydratedRoot.Id, Is.EqualTo(root.Id));
                Assert.That(hydratedRoot.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(hydratedPayload.Label, Is.EqualTo(payload.Label));
                Assert.That(hydratedPayload.Sequence, Is.EqualTo(payload.Sequence));
                CollectionAssert.AreEqual(new[] { "json-cache:7" }, executions.ToArray());
            });
        }

        [Test]
        public void SystemTextJsonSerializerFactory_RepeatedCalls_ReturnSameSerializerType()
        {
            var api = Helper.GetApi(
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());

            var firstSerializer = api.SystemTextSerializerFactory().Serializer();
            var secondSerializer = api.SystemTextSerializerFactory().Serializer();

            Assert.That(secondSerializer.GetType(), Is.EqualTo(firstSerializer.GetType()));
        }

        public sealed class JsonIntegrationPayload : IPayload
        {
            public const string PayloadHandlerKey = "test/integration/json-serializer-v1";

            public string Label { get; set; } = string.Empty;
            public int Sequence { get; set; }
            public string PayloadId => PayloadHandlerKey;
            public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
        }

        private sealed class FailingOriginalHandler : IHandler
        {
            public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException(
                    "The original handler should not execute after JSON cache hydration.");
            }
        }
    }
}
