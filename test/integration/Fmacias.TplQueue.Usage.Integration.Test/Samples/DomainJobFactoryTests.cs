using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using global::Fmacias.TplQueue;
using Microsoft.Extensions.DependencyInjection;
using TplQueue.Sample.Domain.Composition;
using TplQueue.Sample.Etl.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class DomainJobFactoryTests
    {
        [Test]
        public void DomainFactory_SingleRootCopiesMeasurementsAndPreservesOperationIdentity()
        {
            // Arrange
            using var provider = CreateServiceProvider();
            var factory = provider.GetRequiredService<ISampleJobFactory>();
            var measurements = CreateMeasurements().ToList();
            var original = measurements[0];

            // Act
            var first = factory.IngestMeasurementsSigleJobRoot(measurements);
            var second = factory.IngestMeasurementsSigleJobRoot(measurements);
            measurements.Clear();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(first.Id, Is.EqualTo(first.Payload.EtlOperationId));
                Assert.That(second.Id, Is.Not.EqualTo(first.Id));
                Assert.That(first.GetDependentDataJobs(), Is.Empty);
                Assert.That(first.Payload.Measurements, Has.Count.EqualTo(2));
                Assert.That(first.Payload.Measurements[0].Value, Is.EqualTo(original.Value));
                Assert.That(first.Name, Is.EqualTo("Single job: ingest measurements"));
            });
        }

        [Test]
        public void DomainFactory_RejectsInvalidMeasurementCollections()
        {
            // Arrange
            using var provider = CreateServiceProvider();
            var factory = provider.GetRequiredService<ISampleJobFactory>();

            // Act / Assert
            Assert.Multiple(() =>
            {
                Assert.That(() => factory.LoadMeasurementsJobRoot(null!), Throws.ArgumentNullException);
                Assert.That(() => factory.IngestMeasurementsSigleJobRoot(null!), Throws.ArgumentNullException);
                Assert.That(() => factory.LoadMeasurementsJobRoot(Array.Empty<LegacyMeasurement>()), Throws.ArgumentException);
                Assert.That(() => factory.IngestMeasurementsSigleJobRoot(Array.Empty<LegacyMeasurement>()), Throws.ArgumentException);
                Assert.That(() => factory.LoadMeasurementsJobRoot(new LegacyMeasurement[] { null! }), Throws.ArgumentException);
                Assert.That(() => factory.IngestMeasurementsSigleJobRoot(new LegacyMeasurement[] { null! }), Throws.ArgumentException);
            });
        }

        private static ServiceProvider CreateServiceProvider()
        {
            var api = API.Create(
                CoreApi.Create(),
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataJobFactory>(api.DataJobFactory);
            services.AddSingleton(api.RetryPolicyAbstractFactory);
            services.AddSampleDomain();
            return services.BuildServiceProvider();
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
