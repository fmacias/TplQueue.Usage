using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Handlers
{
    internal sealed class IngestMeasurementsHandler : PayloadHandler<IngestMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 500;
        private readonly IEtlExecutionDataStore _store;
        private readonly ILogger<IngestMeasurementsHandler> _logger;

        private IngestMeasurementsHandler(IEtlExecutionDataStore store, ILogger<IngestMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public static IngestMeasurementsHandler Create(IEtlExecutionDataStore store, ILogger<IngestMeasurementsHandler> logger)
        {
            return new IngestMeasurementsHandler(store, logger);
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
            _logIngestedMeasurements(_logger, normalized.Count, payload.PayloadId, payload.EtlOperationId, null);
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

        /// <summary>
        /// Precompiled log message to avoid allocations (CA1848)
        /// </summary>
        private static readonly Action<ILogger, int, string, Guid, Exception?> _logIngestedMeasurements =
            LoggerMessage.Define<int, string, Guid>(
                LogLevel.Information,
                new EventId(1, nameof(IngestMeasurementsHandler)),
                "Ingested {MeasurementCount} measurements from {PayloadId} at Cycle {CycleId}.");

    }
}
