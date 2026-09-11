using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Etl.Contracts.Dto;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class LegacyMeasurementScenarioTests
    {
        [Test]
        public void Run_PassesCollectedDataAndQueueToTheSharedWorkflow()
        {
            var observedAt =
                new DateTime(2026, 7, 29, 12, 30, 0, DateTimeKind.Utc);
            var measurements = new[]
            {
                new LegacyMeasurement(
                    "legacy-sensor",
                    68m,
                    TemperatureUnit.Fahrenheit,
                    observedAt)
            };
            var collector = new StubCollector(measurements);
            var workflow = new RecordingWorkflow();
            var sut = new LegacyMeasurementScenario(collector, workflow);

            var rootJobId = sut.Run(
                AvailableQueue.Cache,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(rootJobId, Is.EqualTo(workflow.RootJobId));
                Assert.That(workflow.Queue, Is.EqualTo(AvailableQueue.Cache));
                Assert.That(workflow.Measurements, Is.SameAs(measurements));
                Assert.That(workflow.Measurements![0].SensorCode,
                    Is.EqualTo("legacy-sensor"));
                Assert.That(workflow.Measurements[0].ObservedAtUtc,
                    Is.EqualTo(observedAt));
            });
        }

        [Test]
        public void Run_PropagatesCancellationBeforeSubmittingWork()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var collector = new StubCollector(Array.Empty<LegacyMeasurement>());
            var workflow = new RecordingWorkflow();
            var sut = new LegacyMeasurementScenario(collector, workflow);

            Assert.That(
                () => sut.Run(AvailableQueue.Parallel, cancellation.Token),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(workflow.Measurements, Is.Null);
        }

        [Test]
        public void Constructor_RejectsMissingDependencies()
        {
            var collector = new StubCollector(Array.Empty<LegacyMeasurement>());
            var workflow = new RecordingWorkflow();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new LegacyMeasurementScenario(null!, workflow),
                    Throws.ArgumentNullException
                        .With.Property("ParamName").EqualTo("collector"));
                Assert.That(
                    () => new LegacyMeasurementScenario(collector, null!),
                    Throws.ArgumentNullException
                        .With.Property("ParamName").EqualTo("workflow"));
            });
        }

        [Test]
        public void Collector_ReturnsDeterministicConsumerOwnedMeasurements()
        {
            var collector = new LegacyMeasurementCollector();

            var measurements = collector.Collect(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(measurements, Has.Count.EqualTo(3));
                Assert.That(
                    measurements.Select(measurement => measurement.SensorCode),
                    Is.EqualTo(new[]
                    {
                        "boiler-outlet",
                        "warehouse-zone-a",
                        "warehouse-zone-b"
                    }));
                Assert.That(
                    measurements.Select(measurement => measurement.Unit),
                    Is.EqualTo(new[]
                    {
                        TemperatureUnit.Celsius,
                        TemperatureUnit.Fahrenheit,
                        TemperatureUnit.Celsius
                    }));
            });
        }

        private sealed class StubCollector : ILegacyMeasurementCollector
        {
            private readonly IReadOnlyList<LegacyMeasurement> _measurements;

            public StubCollector(IReadOnlyList<LegacyMeasurement> measurements)
            {
                _measurements = measurements;
            }

            public IReadOnlyList<LegacyMeasurement> Collect(
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _measurements;
            }
        }

        private sealed class RecordingWorkflow : IEtlWorkflow
        {
            public Guid RootJobId { get; } = Guid.NewGuid();

            public AvailableQueue? Queue { get; private set; }

            public IReadOnlyList<LegacyMeasurement>? Measurements
            {
                get;
                private set;
            }

            public Guid EnqueueMeasurements(
                AvailableQueue availableQueues,
                IReadOnlyList<LegacyMeasurement> legacyMeasurements,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Queue = availableQueues;
                Measurements = legacyMeasurements;
                return RootJobId;
            }

            public bool Cancel(Guid rootJobId)
            {
                return false;
            }
        }
    }
}
