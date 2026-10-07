using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Handlers
{
    internal sealed class TransformMeasurementsHandler : PayloadHandler<TransformMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 700;
        private readonly IEtlExecutionDataStore _store;
        private readonly ILogger<TransformMeasurementsHandler> _logger;

        private TransformMeasurementsHandler(IEtlExecutionDataStore store, ILogger<TransformMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public static TransformMeasurementsHandler Create(IEtlExecutionDataStore store, ILogger<TransformMeasurementsHandler> logger)
        {
            return new TransformMeasurementsHandler(store, logger);
        }
        protected override async Task HandleAsync(
            TransformMeasurementsPayload payload,
            CancellationToken cancellationToken)
        {
            // Keep the real sample operation visible long enough to inspect in the timeline.
            await Task.Delay(
                SampleExecutionDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_store.TryGetMeasurements(payload.EtlOperationId, out var measurements))
            {
                throw new InvalidOperationException(
                    "Transform cannot run because Ingest did not produce measurement data.");
            }

            // Compute numeric values once
            var count = measurements!.Count;
            var average = measurements.Average();
            var min = measurements.Min();
            var max = measurements.Max();

            // Build summary for storing (still needed)
            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "{0} measurements; average {1:F2} C, range {2:F2} C to {3:F2} C",
                count,
                average,
                min,
                max);
            _store.StoreSummary(payload.EtlOperationId, summary);

            // Use LoggerMessage delegate to log structured values without extra allocations
            _transformedMeasurementsLog(_logger, summary, null);
        }

        /// <summary>
        /// LoggerMessage delegate to avoid allocations and expensive formatting when logging is disabled 
        /// </summary>
        private static readonly Action<ILogger, string, Exception?> _transformedMeasurementsLog =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(0, nameof(TransformMeasurementsHandler)),
                "Transformed measurement summary: {Summary}");
    }
}
