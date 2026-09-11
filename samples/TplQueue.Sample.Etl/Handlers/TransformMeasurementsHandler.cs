using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TplQueue.Sample.Etl.Payloads;
using TplQueue.Sample.Etl.Runtime;

namespace TplQueue.Sample.Etl.Handlers
{
    internal sealed class TransformMeasurementsHandler : EtlPayloadHandler<TransformMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 700;

        private readonly EtlExecutionDataStore _store;
        private readonly ILogger<TransformMeasurementsHandler> _logger;

        public TransformMeasurementsHandler(
            EtlExecutionDataStore store,
            ILogger<TransformMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "{0} measurements; average {1:F2} C, range {2:F2} C to {3:F2} C",
                measurements!.Count,
                measurements.Average(),
                measurements.Min(),
                measurements.Max());
            _store.StoreSummary(payload.EtlOperationId, summary);
            _logger.LogInformation("Transformed measurement summary: {Summary}.", summary);
        }
    }
}
