using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TplQueue.Sample.Etl.Payloads;
using TplQueue.Sample.Etl.Runtime;

namespace TplQueue.Sample.Etl.Handlers
{
    internal sealed class LoadMeasurementsHandler : EtlPayloadHandler<LoadMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 400;

        private readonly EtlExecutionDataStore _store;
        private readonly ILogger<LoadMeasurementsHandler> _logger;

        public LoadMeasurementsHandler(
            EtlExecutionDataStore store,
            ILogger<LoadMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task HandleAsync(
            LoadMeasurementsPayload payload,
            CancellationToken cancellationToken)
        {
            // Keep the real sample operation visible long enough to inspect in the timeline.
            await Task.Delay(
                SampleExecutionDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_store.TryGetSummary(payload.EtlOperationId, out var summary))
            {
                throw new InvalidOperationException(
                    "Load cannot run because Transform did not produce a summary.");
            }

            _logger.LogInformation("Loaded measurement summary: {Summary}.", summary);
        }
    }
}
