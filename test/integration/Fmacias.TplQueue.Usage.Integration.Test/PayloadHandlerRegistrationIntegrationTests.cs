using System.Collections.Concurrent;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;

namespace Fmacias.TplQueue.Integration.Test
{
    [TestFixture]
    public sealed class PayloadHandlerRegistrationIntegrationTests
    {
        [Test]
        public async Task CacheHydration_ExecutesPayloadHandlerRegisteredThroughApi()
        {
            var executions = new ConcurrentQueue<string>();
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var api = Helper.GetApi(
                retryPolicyOptions,
                new Dictionary<string, IQOptions>());
            api.RegisterPayloadHandler<ApiRegisteredPayload>(
                ApiRegisteredPayload.PayloadHandlerKey,
                (payload, ct) =>
                {
                    executions.Enqueue(payload.Label);
                    return Task.CompletedTask;
                });
            var payload = new ApiRegisteredPayload
            {
                Label = "api-registered-execution"
            };
            var root = api.DataJobFactory.DataJobRoot(
                payload,
                new FailingOriginalHandler(),
                "api-registered-root",
                () => NoRetryPolicy.Create());
            var cache = api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                api.SystemTexSerializerFactory().Serializer());

            cache.Dehydrate(root, isFifo: false);

            Assert.That(cache.TryHydrateNextJob(out var hydratedRoot, out var lease), Is.True);
            Assert.That(lease.JobId, Is.EqualTo(root.Id));

            using var queue = api.QFactory.Parallel(
                Guid.NewGuid(),
                "api-registered-payload-queue",
                maxParallelism: 1,
                logger: Helper.GetLogger<IParallelQ>(),
                retryPolicyFactory: () => NoRetryPolicy.Create());
            hydratedRoot.Enqueue(queue, CancellationToken.None);
            queue.ResumePolling();

            await hydratedRoot.WaitUntilFinishedAsync();

            Assert.Multiple(() =>
            {
                Assert.That(hydratedRoot.Id, Is.EqualTo(root.Id));
                Assert.That(hydratedRoot.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                CollectionAssert.AreEqual(new[] { "api-registered-execution" }, executions.ToArray());
            });
        }

        public sealed class ApiRegisteredPayload : IPayload
        {
            public const string PayloadHandlerKey = "test/integration/api-registration-v1";

            public string Label { get; set; } = string.Empty;
            public string PayloadId => PayloadHandlerKey;
            public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
        }

        private sealed class FailingOriginalHandler : IHandler
        {
            public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException(
                    "The original dehydration handler should not be used after cache hydration.");
            }
        }
    }
}
