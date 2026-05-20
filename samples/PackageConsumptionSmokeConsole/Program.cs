using System.Collections.Concurrent;
using System.Diagnostics;
using Fmacias.TplQueue;
using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TplQueue.Usage.PackageConsumptionSmokeConsole
{
    internal static class Program
    {
        private static readonly IReadOnlyDictionary<string, SmokeMode> SupportedModes =
            new Dictionary<string, SmokeMode>(StringComparer.OrdinalIgnoreCase)
            {
                ["all"] = SmokeMode.All,
                ["job-root"] = SmokeMode.JobRoot,
                ["parallel-closures"] = SmokeMode.ParallelClosures,
                ["fifo-closures"] = SmokeMode.FifoClosures,
                ["retry"] = SmokeMode.Retry,
                ["observer"] = SmokeMode.Observer,
                ["payload-cache"] = SmokeMode.PayloadCache
            };

        private static async Task<int> Main(string[] args)
        {
            try
            {
                var mode = ParseMode(args);
                if (mode == SmokeMode.All)
                {
                    await RunAllAsync().ConfigureAwait(false);
                }
                else
                {
                    await RunAsync(mode).ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static SmokeMode ParseMode(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return SmokeMode.All;
            }

            if (args.Length != 1 || !SupportedModes.TryGetValue(args[0], out var mode))
            {
                throw new ArgumentException(
                    "Supported modes: all, job-root, parallel-closures, fifo-closures, retry, observer, payload-cache.");
            }

            return mode;
        }

        private static async Task RunAllAsync()
        {
            var orderedModes = new[]
            {
                SmokeMode.JobRoot,
                SmokeMode.ParallelClosures,
                SmokeMode.FifoClosures,
                SmokeMode.Retry,
                SmokeMode.Observer,
                SmokeMode.PayloadCache
            };

            foreach (var mode in orderedModes)
            {
                await RunAsync(mode).ConfigureAwait(false);
            }

            Console.WriteLine();
            Console.WriteLine("All package-consumption smoke scenarios passed.");
        }

        private static async Task RunAsync(SmokeMode mode)
        {
            Console.WriteLine();
            Console.WriteLine($"Running smoke mode '{GetModeName(mode)}'...");

            switch (mode)
            {
                case SmokeMode.JobRoot:
                    await RunJobRootAsync().ConfigureAwait(false);
                    break;
                case SmokeMode.ParallelClosures:
                    await RunParallelClosuresAsync().ConfigureAwait(false);
                    break;
                case SmokeMode.FifoClosures:
                    await RunFifoClosuresAsync().ConfigureAwait(false);
                    break;
                case SmokeMode.Retry:
                    await RunRetryAsync().ConfigureAwait(false);
                    break;
                case SmokeMode.Observer:
                    await RunObserverAsync().ConfigureAwait(false);
                    break;
                case SmokeMode.PayloadCache:
                    await RunPayloadCacheAsync().ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported smoke mode.");
            }

            Console.WriteLine($"PASS '{GetModeName(mode)}'");
        }

        private static async Task RunJobRootAsync()
        {
            var api = CreateApi();
            var executionOrder = new List<string>();

            var child = api.JobFactory.Job(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    executionOrder.Add("child");
                    return Task.CompletedTask;
                },
                name: "smoke-child");

            var root = api.JobFactory.JobRoot(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    executionOrder.Add("root");
                    return Task.CompletedTask;
                },
                name: "smoke-root");

            root.After(child);

            using var queue = CreateParallelQueue(api, "job-root-queue", maxParallelism: 1);
            queue.Enqueue(root, CancellationToken.None);

            await queue.Wait().ConfigureAwait(false);

            Ensure(
                executionOrder.SequenceEqual(new[] { "child", "root" }),
                "The minimal IJob / IJobRoot scenario did not execute in the expected dependency order.");
        }

        private static async Task RunParallelClosuresAsync()
        {
            var api = CreateApi();
            var stopwatch = Stopwatch.StartNew();
            var firstStarted = CreateSignal();
            var secondStarted = CreateSignal();

            using var queue = CreateParallelQueue(api, "parallel-closures-queue", maxParallelism: 2);
            queue.Enqueue(
                async ct =>
                {
                    firstStarted.TrySetResult(true);
                    await secondStarted.Task.WaitAsync(ct).ConfigureAwait(false);
                    await Task.Delay(150, ct).ConfigureAwait(false);
                },
                CancellationToken.None,
                name: "parallel-first");
            queue.Enqueue(
                async ct =>
                {
                    secondStarted.TrySetResult(true);
                    await firstStarted.Task.WaitAsync(ct).ConfigureAwait(false);
                    await Task.Delay(150, ct).ConfigureAwait(false);
                },
                CancellationToken.None,
                name: "parallel-second");

            await queue.Wait().ConfigureAwait(false);
            stopwatch.Stop();

            Ensure(
                stopwatch.ElapsedMilliseconds < 280,
                $"Parallel closures did not overlap as expected. Elapsed: {stopwatch.ElapsedMilliseconds} ms.");
        }

        private static async Task RunFifoClosuresAsync()
        {
            var api = CreateApi();
            var executionOrder = new ConcurrentQueue<string>();

            using var queue = CreateFifoQueue(api, "fifo-closures-queue");
            queue.Enqueue(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    executionOrder.Enqueue("first");
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                name: "fifo-first");
            queue.Enqueue(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    executionOrder.Enqueue("second");
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                name: "fifo-second");
            queue.Enqueue(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    executionOrder.Enqueue("third");
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                name: "fifo-third");

            await queue.Wait().ConfigureAwait(false);

            Ensure(
                executionOrder.SequenceEqual(new[] { "first", "second", "third" }),
                "FIFO closures did not preserve the expected execution order.");
        }

        private static async Task RunRetryAsync()
        {
            var api = CreateApi();
            var dispatcherPolicyInvocations = 0;
            var rootPolicyInvocations = 0;
            var completed = false;

            var root = api.JobFactory.JobRoot(
                body: ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    completed = true;
                    return Task.CompletedTask;
                },
                retryPolicyFactory: () => new CountingRetryPolicy(() => Interlocked.Increment(ref rootPolicyInvocations)),
                name: "retry-root");

            using var queue = CreateParallelQueue(
                api,
                "retry-queue",
                maxParallelism: 1,
                retryPolicyFactory: () => new CountingRetryPolicy(() => Interlocked.Increment(ref dispatcherPolicyInvocations)));
            queue.Enqueue(root, CancellationToken.None);

            await queue.Wait().ConfigureAwait(false);

            Ensure(completed, "The retry scenario never reached a successful completion.");
            Ensure(rootPolicyInvocations >= 1, "The retry scenario did not execute the root-specific retry policy.");
            Ensure(
                dispatcherPolicyInvocations == 0,
                "The dispatcher-level retry policy should not run when the root provides its own retry policy.");
        }

        private static async Task RunObserverAsync()
        {
            var api = CreateApi();
            var observer = new CollectingObserver();
            var root = api.JobFactory.JobRoot(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                name: "observer-root");

            using var queue = CreateParallelQueue(api, "observer-queue", maxParallelism: 1);
            using var subscription = queue.Subscribe(observer);
            queue.Enqueue(root, CancellationToken.None);

            await queue.Wait().ConfigureAwait(false);
            // Queue finalization happens before the observer hub flushes every event to subscribers.
            await Task.Delay(200).ConfigureAwait(false);

            var statuses = observer.Events.Select(evt => evt.Status).ToArray();
            Ensure(statuses.Length > 0, "Observer smoke scenario did not receive any lifecycle event.");
            Ensure(
                statuses.Contains(JobEventStatus.Started) || statuses.Contains(JobEventStatus.Running),
                "Observer smoke scenario did not observe an active execution status.");
            Ensure(
                statuses.Contains(JobEventStatus.RootSuccessed) || statuses.Contains(JobEventStatus.Successed),
                "Observer smoke scenario did not observe a successful terminal status.");
        }

        private static async Task RunPayloadCacheAsync()
        {
            var api = CreateApi();
            var executions = new ConcurrentQueue<string>();
            api.RegisterPayloadHandler<SmokePayload>(
                SmokePayload.PayloadHandlerKey,
                (payload, ct) =>
                {
                    executions.Enqueue(payload.Label);
                    return Task.CompletedTask;
                });

            var root = api.DataJobFactory.DataJobRoot(
                new SmokePayload { Label = "payload-cache-ok" },
                new FailingOriginalHandler(),
                name: "payload-cache-root",
                retryPolicy: () => NoRetryPolicy.Create());

            var cache = api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                api.SystemTextSerializerFactory().Serializer());

            cache.Dehydrate(root, isFifo: false);
            Ensure(
                cache.TryHydrateNextJob(out var hydratedRoot, out var lease),
                "Payload cache smoke scenario could not hydrate the cached root.");
            Ensure(lease.JobId == root.Id, "Hydrated lease does not match the original root id.");

            using var queue = CreateParallelQueue(api, "payload-cache-queue", maxParallelism: 1);
            queue.Enqueue(hydratedRoot, CancellationToken.None);

            await queue.Wait().ConfigureAwait(false);

            Ensure(
                executions.SequenceEqual(new[] { "payload-cache-ok" }),
                "Payload cache smoke scenario did not execute the API-registered payload handler.");
        }

        private static API CreateApi()
        {
            return API.Create(
                CoreApi.Create(),
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());
        }

        private static IParallelQ CreateParallelQueue(
            API api,
            string name,
            int maxParallelism,
            Func<IRetryPolicy>? retryPolicyFactory = null)
        {
            return api.QFactory.Parallel(
                Guid.NewGuid(),
                name,
                maxParallelism,
                NullLoggerFactory.Instance.CreateLogger<IParallelQ>(),
                retryPolicyFactory ?? (() => NoRetryPolicy.Create()));
        }

        private static IFifoQ CreateFifoQueue(
            API api,
            string name,
            Func<IRetryPolicy>? retryPolicyFactory = null)
        {
            return api.QFactory.Fifo(
                Guid.NewGuid(),
                name,
                NullLoggerFactory.Instance.CreateLogger<IFifoQ>(),
                retryPolicyFactory ?? (() => NoRetryPolicy.Create()));
        }

        private static TaskCompletionSource<bool> CreateSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static string GetModeName(SmokeMode mode)
        {
            return mode switch
            {
                SmokeMode.JobRoot => "job-root",
                SmokeMode.ParallelClosures => "parallel-closures",
                SmokeMode.FifoClosures => "fifo-closures",
                SmokeMode.Retry => "retry",
                SmokeMode.Observer => "observer",
                SmokeMode.PayloadCache => "payload-cache",
                _ => "all"
            };
        }

        private static void Ensure(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private enum SmokeMode
        {
            All,
            JobRoot,
            ParallelClosures,
            FifoClosures,
            Retry,
            Observer,
            PayloadCache
        }

        private sealed class CollectingObserver : IObserver<IJobEvent>
        {
            private readonly ConcurrentQueue<IJobEvent> _events = new ConcurrentQueue<IJobEvent>();

            public IReadOnlyCollection<IJobEvent> Events => _events.ToArray();
            public Exception? Error { get; private set; }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
                Error = error;
            }

            public void OnNext(IJobEvent value)
            {
                if (value != null)
                {
                    _events.Enqueue(value);
                }
            }
        }

        private sealed class SmokePayload : IPayload
        {
            public const string PayloadHandlerKey = "smoke/payload-cache/v1";

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

        private sealed class CountingRetryPolicy : IRetryPolicy
        {
            private readonly Action _onExecute;

            public CountingRetryPolicy(Action onExecute)
            {
                _onExecute = onExecute;
            }

            public int RetryCount { get; private set; }

            public Task<TResult> ExecuteAsync<TResult>(
                Func<CancellationToken, Task<TResult>> action,
                CancellationToken cancellationToken)
            {
                RetryCount++;
                _onExecute?.Invoke();
                return action(cancellationToken);
            }

            public IRetryPolicy SetFromDescriptor(IRetryPolicyOptions descriptor)
            {
                throw new NotSupportedException("The smoke retry policy is local to the sample application.");
            }

            public IRetryPolicy SetFromOptions(RetryPolicyOptions options)
            {
                throw new NotSupportedException("The smoke retry policy is local to the sample application.");
            }

            public IRetryPolicyOptions ToDescriptor()
            {
                throw new NotSupportedException("The smoke retry policy is local to the sample application.");
            }
        }
    }
}
