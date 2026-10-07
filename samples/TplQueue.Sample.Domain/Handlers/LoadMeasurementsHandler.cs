using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Handlers
{
    internal sealed class LoadMeasurementsHandler : PayloadHandler<LoadMeasurementsPayload>
    {
        private const int SampleExecutionDelayMilliseconds = 400;
        private readonly IEtlExecutionDataStore _store;
        private readonly ILogger<LoadMeasurementsHandler> _logger;


        private LoadMeasurementsHandler(IEtlExecutionDataStore store, ILogger<LoadMeasurementsHandler> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public static LoadMeasurementsHandler Create(IEtlExecutionDataStore store, ILogger<LoadMeasurementsHandler> logger)       
        {   
            return new LoadMeasurementsHandler(store, logger);
        }

        protected override async Task HandleAsync(
            LoadMeasurementsPayload payload,
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
