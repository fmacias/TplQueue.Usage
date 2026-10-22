using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Extensions;
using System.Security.Cryptography;
using TplQueue.Sample.Domain.Handlers;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Factories
{
    internal sealed class SampleJobFactory : ISampleJobFactory
    {
        private readonly IDataJobFactory _dataJobFactory;
        private readonly IRetryPolicyAbstractFactory _retryPolicyAbstractFactory;
        private readonly IEtlExecutionDataStore _store;
        private readonly IngestMeasurementsHandler _ingestHandler;
        private readonly TransformMeasurementsHandler _transformHandler;
        private readonly LoadMeasurementsHandler _loadHandler;
        public SampleJobFactory(IDataJobFactory dataJobFactory,
            IRetryPolicyAbstractFactory retryPolicyAbstractFactory,
            IEtlExecutionDataStore etlExecutionDataStore,
            IngestMeasurementsHandler ingestHandler,
            TransformMeasurementsHandler transformHandler,
            LoadMeasurementsHandler loadHandler)
        {
            _dataJobFactory = dataJobFactory ?? throw new ArgumentNullException(nameof(dataJobFactory));
            _retryPolicyAbstractFactory = retryPolicyAbstractFactory ?? throw new ArgumentNullException(nameof(retryPolicyAbstractFactory));
            _store = etlExecutionDataStore ?? throw new ArgumentNullException(nameof(etlExecutionDataStore));
            _ingestHandler = ingestHandler;
            _transformHandler = transformHandler;
            _loadHandler = loadHandler;
        }

        /// <summary>Builds the fixed ETL graph without replacing caller-supplied measurements.</summary>
        public IDataJobRoot<ILoadMeasurementsPayload> LoadMeasurementsJobRoot(
            IReadOnlyList<LegacyMeasurement> measurements)
        {
            var etlOperationId = Guid.NewGuid();
            var ingestPayload = IngestMeasurementsPayload.Create(measurements, etlOperationId);
            var collectionTime = ingestPayload.CollectionTime;

            IDataJob<IIngestMeasurementsPayload> ingest = GetIngest(ingestPayload);
            IDataJob<ITransformMeasurementsPayload> transform = GetTransform(etlOperationId, collectionTime);
            IDataJobRoot<ILoadMeasurementsPayload> load = GetLoad(etlOperationId, collectionTime);

            return ingest.Then(transform).Then(load);
        }

        /// <summary>Uses the root identity as the operation identity for a single ingest job.</summary>
        public IDataJobRoot<IIngestMeasurementsPayload> IngestMeasurementsSigleJobRoot(
            IReadOnlyList<LegacyMeasurement> measurements)
        {
            var ingestPayload = IngestMeasurementsPayload.Create(measurements, Guid.NewGuid());
            return GetSingleIngest(ingestPayload);
        }


        private IDataJobRoot<ILoadMeasurementsPayload> GetLoad(Guid etlOperationId, DateTime collectionTime)
        {
            return _dataJobFactory.DataJobRoot<ILoadMeasurementsPayload>(
                            etlOperationId,
                            LoadMeasurementsPayload.Create(collectionTime, etlOperationId),
                            _loadHandler,
                            "Load measurement summary",
                            () => _retryPolicyAbstractFactory.GetPolicy<IExponentialBackoff>());
        }

        private IDataJob<ITransformMeasurementsPayload> GetTransform(Guid etlOperationId, DateTime collectionTime)
        {
            return _dataJobFactory.DataJob<ITransformMeasurementsPayload>(
                TransformMeasurementsPayload.Create(collectionTime, etlOperationId),
                _transformHandler,
                "Transform measurements");
        }

        private IDataJob<IIngestMeasurementsPayload> GetIngest(IIngestMeasurementsPayload ingestPayload)
        {
            return _dataJobFactory.DataJob(ingestPayload,_ingestHandler,"Ingest measurements");
        }
        private IDataJobRoot<IIngestMeasurementsPayload> GetSingleIngest(IngestMeasurementsPayload ingestPayload)
        {
            return _dataJobFactory.DataJobRoot<IIngestMeasurementsPayload>(
                            ingestPayload.EtlOperationId,
                            ingestPayload,
                            _ingestHandler,
                            "Single job: ingest measurements");
        }

        /// <summary>
        /// Generates a random decimal in the specified inclusive range with two decimal places.
        /// </summary>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>A random decimal between the bounds.</returns>
        private static decimal GenerateRandomDecimal(double min, double max)
        {
            decimal secureDecimalResult;

            int hundredthMinimum = (int)(min * 100);
            int hundredthMaximum = (int)(max * 100);

            int totalPossibleValues = (hundredthMaximum - hundredthMinimum) + 1;

            using (var secureCryptoProvider = RandomNumberGenerator.Create())
            {
                byte[] randomBuffer = new byte[4];
                secureCryptoProvider.GetBytes(randomBuffer);

                //Die rohen Bytes in eine normale Zahl umwandeln und das Minuszeichen herausfiltern
                int rawPositiveValue = BitConverter.ToInt32(randomBuffer, 0) & int.MaxValue;
                int standardValueFromZero = rawPositiveValue % totalPossibleValues;
                int finalScaledHundredths = hundredthMinimum + standardValueFromZero;
                secureDecimalResult = finalScaledHundredths / 100m;
            }

            return secureDecimalResult;
        }
    }
}
