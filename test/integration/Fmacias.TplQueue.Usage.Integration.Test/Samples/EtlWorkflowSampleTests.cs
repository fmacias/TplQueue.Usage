using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Etl.Contracts.Dto;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class EtlWorkflowSampleTests
    {
        [Test]
        public void EnqueueMeasurements_BuildsTheFixedPayloadGraph()
        {
            using var provider = CreateServiceProvider(
                out _,
                out var parallelQueue,
                out _);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();
            var measurements = CreateMeasurements();

            var rootJobId = workflow.EnqueueMeasurements(
                AvailableQueue.Parallel,
                measurements,
                CancellationToken.None);

            var root = parallelQueue.LastDataJobRoot;
            Assert.That(root, Is.Not.Null);
            var jobs = ExpandGraph(root!);

            Assert.Multiple(() =>
            {
                Assert.That(rootJobId, Is.EqualTo(root!.Id));
                Assert.That(jobs.Select(job => provider.GetRequiredService<ISimulationGraphCatalog>().GetRootJobIds(job.Id)),
                    Is.All.EqualTo(new[] { rootJobId }));
                Assert.That(parallelQueue.GenericDataJobEnqueueCount, Is.Zero);
                Assert.That(jobs.Select(job => job.Name), Is.EqualTo(new[]
                {
                    "Load measurement summary",
                    "Transform measurements",
                    "Ingest measurements"
                }));
                Assert.That(root.GetDependentDataJobs(), Has.Count.EqualTo(1));
                Assert.That(
                    root.GetDependentDataJobs().Single().GetDependentDataJobs(),
                    Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void EnqueueMeasurements_SelectsTheRequestedQueue()
        {
            using var provider = CreateServiceProvider(
                out var fifoQueue,
                out var parallelQueue,
                out var cacheQueue);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();

            workflow.EnqueueMeasurements(
                AvailableQueue.FIFO,
                CreateMeasurements(),
                CancellationToken.None);
            workflow.EnqueueMeasurements(
                AvailableQueue.Parallel,
                CreateMeasurements(),
                CancellationToken.None);
            workflow.EnqueueMeasurements(
                AvailableQueue.Cache,
                CreateMeasurements(),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(fifoQueue.GenericDataJobEnqueueCount, Is.EqualTo(0));
                Assert.That(parallelQueue.GenericDataJobEnqueueCount, Is.EqualTo(0));
                Assert.That(cacheQueue.GenericDataJobEnqueueCount, Is.EqualTo(1));
                Assert.That(fifoQueue.LastDataJobRoot, Is.Not.Null);
            });
        }

        [Test]
        public void EnqueueMeasurements_RejectsMissingOrEmptyMeasurements()
        {
            using var provider = CreateServiceProvider(out _, out _, out _);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => workflow.EnqueueMeasurements(
                        AvailableQueue.Parallel,
                        null!,
                        CancellationToken.None),
                    Throws.ArgumentNullException
                        .With.Property("ParamName").EqualTo("legacyMeasurements"));
                Assert.That(
                    () => workflow.EnqueueMeasurements(
                        AvailableQueue.Parallel,
                        Array.Empty<LegacyMeasurement>(),
                        CancellationToken.None),
                    Throws.TypeOf<ArgumentException>()
                        .With.Property("ParamName").EqualTo("legacyMeasurements"));
            });
        }

        [Test]
        public void Dispose_IsIdempotent_AndPreventsFurtherEnqueueing()
        {
            using var provider = CreateServiceProvider(out _, out _, out _);
            var workflow = provider.GetRequiredService<IEtlWorkflow>();
            var disposableWorkflow = (IDisposable)workflow;

            disposableWorkflow.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(() => disposableWorkflow.Dispose(), Throws.Nothing);
                Assert.That(
                    () => workflow.EnqueueMeasurements(
                        AvailableQueue.Parallel,
                        CreateMeasurements(),
                        CancellationToken.None),
                    Throws.TypeOf<ObjectDisposedException>());
            });
        }

        private static ServiceProvider CreateServiceProvider(
            out EtlQueueRuntimeCancellationCleanupTests.QueueProxy fifoQueueProxy,
            out EtlQueueRuntimeCancellationCleanupTests.QueueProxy parallelQueueProxy,
            out EtlQueueRuntimeCancellationCleanupTests.QueueProxy cacheQueueProxy)
        {
            var fifoQueue = CreateQueue<IFifoQ>(out fifoQueueProxy);
            var parallelQueue = CreateQueue<IParallelQ>(out parallelQueueProxy);
            var cacheQueue = CreateQueue<ICacheQ>(out cacheQueueProxy);
            var api = API.Create(
                CoreApi.Create(),
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataJobFactory>(api.DataJobFactory);
            services.AddSingleton(api.RetryPolicyAbstractFactory);
            services.AddSingleton(fifoQueue);
            services.AddSingleton(parallelQueue);
            services.AddSingleton(cacheQueue);
            services.AddSampleEtlWorkflow();
            return services.BuildServiceProvider();
        }

        private static TQueue CreateQueue<TQueue>(
            out EtlQueueRuntimeCancellationCleanupTests.QueueProxy queueProxy)
            where TQueue : class, IQ
        {
            var queue = DispatchProxy.Create<
                TQueue,
                EtlQueueRuntimeCancellationCleanupTests.QueueProxy>();
            queueProxy =
                (EtlQueueRuntimeCancellationCleanupTests.QueueProxy)(object)queue;
            return queue;
        }

        private static IReadOnlyList<IDataJobNode> ExpandGraph(IDataJobRoot root)
        {
            var jobs = new List<IDataJobNode>();
            AddNode(root, jobs);
            return jobs;
        }

        private static void AddNode(
            IDataJobNode node,
            ICollection<IDataJobNode> jobs)
        {
            jobs.Add(node);
            foreach (var dependency in node.GetDependentDataJobs())
            {
                AddNode(dependency, jobs);
            }
        }

        private static IReadOnlyList<LegacyMeasurement> CreateMeasurements()
        {
            return new[]
            {
                new LegacyMeasurement(
                    "sensor-1",
                    21.5m,
                    TemperatureUnit.Celsius,
                    new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc)),
                new LegacyMeasurement(
                    "sensor-2",
                    70m,
                    TemperatureUnit.Fahrenheit,
                    new DateTime(2026, 7, 29, 10, 0, 1, DateTimeKind.Utc))
            };
        }
    }
}
