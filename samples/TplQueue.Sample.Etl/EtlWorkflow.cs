using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Extensions;
using System;
using System.Collections.Generic;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Etl.Contracts.Dto;
using TplQueue.Sample.Etl.Handlers;
using TplQueue.Sample.Etl.Payloads;
using TplQueue.Sample.Etl.Runtime;

namespace TplQueue.Sample.Etl
{
    internal sealed class EtlWorkflow : IEtlWorkflow, IDisposable
    {
        private readonly EtlQueueRuntime _queueRuntime;
        private readonly IDataJobFactory _dataJobFactory;
        private readonly IngestMeasurementsHandler _ingestHandler;
        private readonly TransformMeasurementsHandler _transformHandler;
        private readonly LoadMeasurementsHandler _loadHandler;
        private readonly IRetryPolicyAbstractFactory _retryPolicyAbstractFactory;
        private bool _disposed;

        public EtlWorkflow(
            EtlQueueRuntime queueRuntime,
            IDataJobFactory dataJobFactory,
            IRetryPolicyAbstractFactory retryPolicyFactory,
            IngestMeasurementsHandler ingestHandler,
            TransformMeasurementsHandler transformHandler,
            LoadMeasurementsHandler loadHandler)
        {
            _queueRuntime = queueRuntime ?? throw new ArgumentNullException(nameof(queueRuntime));
            _dataJobFactory = dataJobFactory ?? throw new ArgumentNullException(nameof(dataJobFactory));
            _ingestHandler = ingestHandler ?? throw new ArgumentNullException(nameof(ingestHandler));
            _transformHandler = transformHandler ?? throw new ArgumentNullException(nameof(transformHandler));
            _loadHandler = loadHandler ?? throw new ArgumentNullException(nameof(loadHandler));
            _retryPolicyAbstractFactory = retryPolicyFactory ?? throw new ArgumentNullException(nameof(retryPolicyFactory));
        }

        public Guid EnqueueMeasurements(
            AvailableQueue availableQueue,
            IReadOnlyList<LegacyMeasurement> legacyMeasurements, 
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (legacyMeasurements == null)
                throw new ArgumentNullException(nameof(legacyMeasurements));

            if (legacyMeasurements.Count == 0)
            {
                throw new ArgumentException(
                    "At least one legacy measurement is required.",
                    nameof(legacyMeasurements));
            }
            var rootJob = CreateDataJobRoot(legacyMeasurements);

            _queueRuntime.Enqueue(availableQueue, rootJob, cancellationToken);
            return rootJob.Id;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _disposed = true;
            }
        }

        private IDataJobRoot<LoadMeasurementsPayload> CreateDataJobRoot(
            IReadOnlyList<LegacyMeasurement> legacyMeasurements)
        {
            var etlOperationId = Guid.NewGuid();
            var ingestPayload = IngestMeasurementsPayload.Create(
                legacyMeasurements,
                etlOperationId);

            var ingest = _dataJobFactory.DataJob(
                ingestPayload,
                _ingestHandler,
                "Ingest measurements");

            var transform = _dataJobFactory.DataJob(
                TransformMeasurementsPayload.Create(
                    ingestPayload.CollectionTime,
                    etlOperationId),
                _transformHandler,
                "Transform measurements");

            var load = _dataJobFactory.DataJobRoot(
                etlOperationId,
                LoadMeasurementsPayload.Create(
                    ingestPayload.CollectionTime,
                    etlOperationId),
                _loadHandler,
                "Load measurement summary",
                () => _retryPolicyAbstractFactory.GetPolicy<IExponentialBackoff>());

            return (IDataJobRoot<LoadMeasurementsPayload>)
                ingest.Then(transform).Then(load);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EtlWorkflow));
            }
        }

        public bool Cancel(Guid rootJobId)
        {
            ThrowIfDisposed();

            return _queueRuntime.Cancel(rootJobId);
        }
    }
}
