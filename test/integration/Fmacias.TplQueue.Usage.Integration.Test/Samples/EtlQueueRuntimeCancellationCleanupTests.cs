using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Etl.Contracts.Dto;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class EtlQueueRuntimeCancellationCleanupTests
    {
        [TestCase(JobEventStatus.RootSuccessed)]
        [TestCase(JobEventStatus.Failed)]
        [TestCase(JobEventStatus.Canceled)]
        public void CacheRootTerminalEvent_ReleasesCancellationAndUsesGenericCacheEnqueue(
            JobEventStatus terminalStatus)
        {
            using var provider = CreateServiceProvider(out var cacheQueueProxy);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();
            cacheQueueProxy.TerminalStatus = terminalStatus;

            var rootJobId = EnqueueCacheRoot(workflow);

            Assert.Multiple(() =>
            {
                Assert.That(cacheQueueProxy.GenericDataJobEnqueueCount, Is.EqualTo(1));
                Assert.That(workflow.Cancel(rootJobId), Is.False);
            });
        }

        [Test]
        public void CacheEnqueueFailure_ReleasesCancellationBeforeRethrowing()
        {
            using var provider = CreateServiceProvider(out var cacheQueueProxy);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();
            cacheQueueProxy.EnqueueException =
                new InvalidOperationException("Simulated cache enqueue failure.");

            Assert.That(
                () => EnqueueCacheRoot(workflow),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Simulated cache enqueue failure."));
            Assert.That(cacheQueueProxy.LastDataJobRoot, Is.Not.Null);
            Assert.That(
                workflow.Cancel(cacheQueueProxy.LastDataJobRoot!.Id),
                Is.False,
                "Failed enqueue state must not remain cancellable.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CachePayloadGraph_RoundTripsThroughRegisteredResolverAndSerializer(
            bool useAssemblyQualifiedName)
        {
            using var provider = CreateServiceProvider(out var cacheQueueProxy);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();
            var resolver = provider.GetRequiredService<ITypeResolver>();
            var serializer =
                provider.GetRequiredService<ISystemTextJsonUniversalSerializer>();

            EnqueueCacheRoot(workflow);

            var payloadNodes = ExpandPayloadGraph(cacheQueueProxy.LastDataJobRoot!);
            var hydratedTypes = new List<Type>();
            var hydratedPayloads = new List<object>();

            foreach (var payloadNode in payloadNodes)
            {
                var payloadTypeName = useAssemblyQualifiedName
                    ? payloadNode.PayloadType.AssemblyQualifiedName!
                    : payloadNode.PayloadType.FullName!;
                var resolvedType = resolver.Resolve(payloadTypeName);
                var serializedPayload = serializer.Serialize(
                    payloadNode.GetPayload(),
                    resolvedType);
                var hydratedPayload = serializer.Deserialize(
                    serializedPayload,
                    resolvedType);

                hydratedTypes.Add(hydratedPayload.GetType());
                hydratedPayloads.Add(hydratedPayload);
            }

            var hydratedIngestPayload = hydratedPayloads.Single(
                payload => payload.GetType().Name == "IngestMeasurementsPayload");
            var hydratedMeasurements =
                (IEnumerable<MeasurementSnapshot>)hydratedIngestPayload
                    .GetType()
                    .GetProperty("Measurements")!
                    .GetValue(hydratedIngestPayload)!;
            var hydratedMeasurement = hydratedMeasurements.Single();

            Assert.Multiple(() =>
            {
                Assert.That(workflow.GetType().Assembly.GetName().Name,
                    Is.EqualTo("TplQueue.Sample.Simulation"));
                Assert.That(hydratedTypes.Select(type => type.Namespace),
                    Is.All.EqualTo("TplQueue.Sample.Simulation.Payloads"));
                Assert.That(typeof(IEtlWorkflow).Assembly.GetName().Name,
                    Is.EqualTo("TplQueue.Sample.Etl.Contracts"));
                Assert.That(
                    hydratedTypes,
                    Is.EquivalentTo(payloadNodes.Select(node => node.PayloadType)));
                Assert.That(hydratedMeasurement.SensorCode, Is.EqualTo("sensor-1"));
                Assert.That(hydratedMeasurement.Value, Is.EqualTo(77m));
                Assert.That(
                    hydratedMeasurement.Unit,
                    Is.EqualTo(TemperatureUnit.Fahrenheit));
                Assert.That(
                    hydratedMeasurement.ObservedAtUtc,
                    Is.EqualTo(new DateTime(
                        2026,
                        1,
                        1,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc)));
            });
        }

        private static ServiceProvider CreateServiceProvider(
            out QueueProxy cacheQueueProxy)
        {
            var fifoQueue = CreateQueue<IFifoQ>(out _);
            var parallelQueue = CreateQueue<IParallelQ>(out _);
            var cacheQueue = CreateQueue<ICacheQ>(out cacheQueueProxy);
            var api = API.Create(
                CoreApi.Create(),
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IApi>(api);
            services.AddSingleton<IDataJobFactory>(api.DataJobFactory);
            services.AddSingleton(api.RetryPolicyAbstractFactory);
            services.AddSingleton(api.SystemTextSerializerFactory());
            services.AddTransient<ISystemTextJsonUniversalSerializer>(
                serviceProvider => serviceProvider
                    .GetRequiredService<ISystemTextJsonSerializerFactory>()
                    .Serializer(new JsonSerializerOptions()));
            services.AddSingleton<IFifoQ>(fifoQueue);
            services.AddSingleton<IParallelQ>(parallelQueue);
            services.AddSingleton<ICacheQ>(cacheQueue);
            services.AddSampleEtlWorkflow();
            return services.BuildServiceProvider();
        }

        private static Guid EnqueueCacheRoot(IEtlWorkflow workflow)
        {
            return workflow.EnqueueMeasurements(
                AvailableQueue.Cache,
                new[]
                {
                    new LegacyMeasurement(
                        "sensor-1",
                        77m,
                        TemperatureUnit.Fahrenheit,
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                },
                CancellationToken.None);
        }

        private static IReadOnlyList<IDataJobNode> ExpandPayloadGraph(
            IDataJobRoot root)
        {
            var nodes = new List<IDataJobNode>();
            AddNodeAndDependencies(root, nodes);
            return nodes;
        }

        private static void AddNodeAndDependencies(
            IDataJobNode node,
            ICollection<IDataJobNode> nodes)
        {
            nodes.Add(node);

            foreach (var dependency in node.GetDependentDataJobs())
            {
                AddNodeAndDependencies(dependency, nodes);
            }
        }

        private static TQueue CreateQueue<TQueue>(out QueueProxy queueProxy)
            where TQueue : class, IQ
        {
            var queue = DispatchProxy.Create<TQueue, QueueProxy>();
            queueProxy = (QueueProxy)(object)queue;
            return queue;
        }

        public class QueueProxy : DispatchProxy
        {
            private readonly object _sync = new object();
            private readonly List<IObserver<IJobEvent>> _observers =
                new List<IObserver<IJobEvent>>();
            private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
            private Func<IJobEvent, Task> _onJobEventChanged =
                _ => Task.CompletedTask;
            private bool _isDisposed;
            private int _leasingPulseMs = 100;

            public int GenericDataJobEnqueueCount { get; private set; }

            public IDataJobRoot? LastDataJobRoot { get; private set; }

            public JobEventStatus TerminalStatus { get; set; } =
                JobEventStatus.RootSuccessed;

            public Exception? EnqueueException { get; set; }

            protected override object? Invoke(
                MethodInfo? targetMethod,
                object?[]? arguments)
            {
                if (targetMethod == null)
                {
                    throw new ArgumentNullException(nameof(targetMethod));
                }

                var args = arguments ?? Array.Empty<object>();

                if (targetMethod.Name == nameof(IObservable<IJobEvent>.Subscribe))
                {
                    if (args.Length != 1 ||
                        args[0] is not IObserver<IJobEvent> observer)
                    {
                        throw new ArgumentException(
                            "Subscribe requires an IJobEvent observer.",
                            nameof(arguments));
                    }

                    return Subscribe(observer);
                }

                if (targetMethod.Name == nameof(IQ.Enqueue) &&
                    args.Length > 0 &&
                    args[0] is IJobRoot root)
                {
                    if (args[0] is IDataJobRoot dataJobRoot)
                    {
                        LastDataJobRoot = dataJobRoot;

                        if (targetMethod.IsGenericMethod)
                        {
                            GenericDataJobEnqueueCount++;
                        }
                    }

                    if (EnqueueException != null)
                    {
                        throw EnqueueException;
                    }

                    Publish(new RootTerminalEvent(root, TerminalStatus));
                    return this;
                }

                switch (targetMethod.Name)
                {
                    case "get_QueueId":
                        return Guid.Parse("418f2f3f-1559-4e78-9c93-1ff66586f057");
                    case "get_Name":
                        return "Test queue";
                    case "get_MaxParallelism":
                        return 1;
                    case "get_IsDisposed":
                        return _isDisposed;
                    case "get_Semaphore":
                        return _semaphore;
                    case "get_RetryPolicyFactory":
                        return (Func<IRetryPolicy>)(() => NoRetryPolicy.Create());
                    case "get_OnJobEventChanged":
                        return _onJobEventChanged;
                    case "set_OnJobEventChanged":
                        _onJobEventChanged =
                            args[0] as Func<IJobEvent, Task> ??
                            throw new ArgumentException(
                                "OnJobEventChanged requires a callback.",
                                nameof(arguments));
                        return null;
                    case "get_LeasingPulseMs":
                        return _leasingPulseMs;
                    case "set_LeasingPulseMs":
                        _leasingPulseMs =
                            args[0] is int leasingPulseMs
                                ? leasingPulseMs
                                : throw new ArgumentException(
                                    "LeasingPulseMs requires an integer.",
                                    nameof(arguments));
                        return null;
                    case nameof(IQ.WaitAsync):
                        return Task.CompletedTask;
                    case nameof(IDisposable.Dispose):
                        _isDisposed = true;
                        return null;
                }

                if (targetMethod.ReturnType == typeof(void))
                {
                    return null;
                }

                if (targetMethod.ReturnType.IsInstanceOfType(this))
                {
                    return this;
                }

                return targetMethod.ReturnType.IsValueType
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
            }

            private IDisposable Subscribe(IObserver<IJobEvent> observer)
            {
                if (observer == null) throw new ArgumentNullException(nameof(observer));

                lock (_sync)
                {
                    _observers.Add(observer);
                }

                return new Subscription(this, observer);
            }

            private void Publish(IJobEvent jobEvent)
            {
                IObserver<IJobEvent>[] observers;

                lock (_sync)
                {
                    observers = _observers.ToArray();
                }

                foreach (var observer in observers)
                {
                    observer.OnNext(jobEvent);
                }
            }

            private void Unsubscribe(IObserver<IJobEvent> observer)
            {
                lock (_sync)
                {
                    _observers.Remove(observer);
                }
            }

            private sealed class Subscription : IDisposable
            {
                private QueueProxy? _owner;
                private readonly IObserver<IJobEvent> _observer;

                public Subscription(
                    QueueProxy owner,
                    IObserver<IJobEvent> observer)
                {
                    _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                    _observer = observer ?? throw new ArgumentNullException(nameof(observer));
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref _owner, null)?.Unsubscribe(_observer);
                }
            }
        }

        private sealed class RootTerminalEvent : IJobEvent
        {
            public RootTerminalEvent(
                IJobRoot root,
                JobEventStatus terminalStatus)
            {
                JobInfo = root ?? throw new ArgumentNullException(nameof(root));
                Status = terminalStatus;
                Timestamp = DateTime.UtcNow;
            }

            public JobEventStatus Status { get; }

            public IJobInfo JobInfo { get; }

            public Exception? Exception => null;

            public DateTime Timestamp { get; }

            public int RetryCount => 0;

            public override string ToString()
            {
                return $"{JobInfo.Id}:{Status}";
            }
        }
    }
}
