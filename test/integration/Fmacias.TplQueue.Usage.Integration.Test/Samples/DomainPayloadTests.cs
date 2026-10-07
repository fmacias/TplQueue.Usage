using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Etl.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class DomainPayloadTests
{
    [Test]
    public void HydrationConstructor_RejectsNullMeasurementElements()
    {
        // Arrange: hydration can bypass the factory's input validation.
        var measurements = new MeasurementSnapshot[] { null! };

        // Act / Assert
        Assert.That(() => new IngestMeasurementsPayload("payload", DateTime.UtcNow,
            Guid.NewGuid(), measurements), Throws.ArgumentException.With.Property("ParamName").EqualTo("measurements"));
    }

    [Test]
    public void HydrationConstructor_CopiesTheMeasurementCollection()
    {
        // Arrange
        var measurement = new MeasurementSnapshot("sensor", 20m, TemperatureUnit.Celsius, DateTime.UtcNow);
        var measurements = new List<MeasurementSnapshot> { measurement };

        // Act
        var payload = new IngestMeasurementsPayload("payload", DateTime.UtcNow, Guid.NewGuid(), measurements);
        measurements.Clear();

        // Assert
        Assert.That(payload.Measurements, Is.EqualTo(new[] { measurement }));
    }
}
