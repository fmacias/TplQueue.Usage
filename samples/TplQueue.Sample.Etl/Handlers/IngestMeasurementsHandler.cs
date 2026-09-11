using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Etl.Payloads;
using TplQueue.Sample.Etl.Runtime;

namespace TplQueue.Sample.Etl.Handlers
{
    internal sealed class IngestMeasurementsHandler : EtlPayloadHandler<IngestMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 500;

        private readonly EtlExecutionDataStore _store;
        private readonly ILogger<IngestMeasurementsHandler> _logger;

        public IngestMeasurementsHandler(
            EtlExecutionDataStore store,
            ILogger<IngestMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task HandleAsync(
            IngestMeasurementsPayload payload,
            CancellationToken cancellationToken)
        {
            // Keep the real sample operation visible long enough to inspect in the timeline.
            await Task.Delay(
                SampleExecutionDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var normalized = new List<decimal>(payload.Measurements.Count);
            foreach (var measurement in payload.Measurements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                normalized.Add(ToCelsius(measurement.Value, measurement.Unit));
            }

            _store.StoreMeasurements(payload.EtlOperationId, normalized);
            _logger.LogInformation(
                "Ingested {MeasurementCount} measurements from {PayloadId} at Cycle {CycleId}.",
                normalized.Count,
                payload.PayloadId,
                payload.EtlOperationId);
        }

        private static decimal ToCelsius(decimal value, TemperatureUnit unit)
        {
            if (unit == TemperatureUnit.Celsius) return value;
            if (unit == TemperatureUnit.Fahrenheit)
            {
                return (value - 32m) * 5m / 9m;
            }

            throw new InvalidOperationException($"Measurement unit '{unit}' is not supported by the sample workflow.");
        }
    }
}
