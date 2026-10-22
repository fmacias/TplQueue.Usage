using Microsoft.Extensions.Logging;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Handlers
{
    internal sealed class LoadMeasurementsHandler : PayloadHandler<ILoadMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 400;
        private readonly IEtlExecutionDataStore _store;
        private readonly ILogger<LoadMeasurementsHandler> _logger;
        
        public LoadMeasurementsHandler(IEtlExecutionDataStore store, ILogger<LoadMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task HandleAsync(
            ILoadMeasurementsPayload payload,
            CancellationToken cancellationToken)
        {
            await Task.Delay(
                SampleExecutionDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (!_store.TryGetSummary(payload.EtlOperationId, out var summary))
            {
                throw new InvalidOperationException(
                    "Load cannot run because Transform did not produce a summary.");
            }

            _loadedMeasurementsLog(_logger, summary!, null);
            _store.Remove(payload.EtlOperationId);

        }
        /// <summary>
        /// Precompiled log message to avoid allocations (CA1848)
        /// </summary>
        private static readonly Action<ILogger, string, Exception?> _loadedMeasurementsLog =
             LoggerMessage.Define<string>(
                 LogLevel.Information,
                 new EventId(0, nameof(LoadMeasurementsHandler)),
                 "Loaded measurement summary: {Summary}.");
    }
}
