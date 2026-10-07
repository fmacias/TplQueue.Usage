using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
        private readonly ILogger<IngestMeasurementsHandler> _ingestLogger;
        private readonly ILogger<TransformMeasurementsHandler> _transformLogger;
        private readonly ILogger<LoadMeasurementsHandler> _loadLogger;
        public SampleJobFactory(IDataJobFactory dataJobFactory,
            IRetryPolicyAbstractFactory retryPolicyAbstractFactory,
            IEtlExecutionDataStore etlExecutionDataStore,
            ILogger<IngestMeasurementsHandler> ingestLogger,
            ILogger<TransformMeasurementsHandler> transformLogger,
            ILogger<LoadMeasurementsHandler> loadLogger)
        {
            _dataJobFactory = dataJobFactory ?? throw new ArgumentNullException(nameof(dataJobFactory));
            _retryPolicyAbstractFactory = retryPolicyAbstractFactory ?? throw new ArgumentNullException(nameof(retryPolicyAbstractFactory));
            _store = etlExecutionDataStore ?? throw new ArgumentNullException(nameof(etlExecutionDataStore));
            _ingestLogger = ingestLogger ?? throw new ArgumentNullException(nameof(ingestLogger));
            _transformLogger = transformLogger ?? throw new ArgumentNullException(nameof(transformLogger));
            _loadLogger = loadLogger ?? throw new ArgumentNullException(nameof(loadLogger));
        }

        public IDataJobRoot<ILoadMeasurementsPayload> LoadMeasurementsJobRoot()
            => LoadMeasurementsJobRoot(LegacyMeasurements(DateTime.UtcNow));

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

        public IDataJobRoot<IIngestMeasurementsPayload> IngestMeasurementsSigleJobRoot()
            => IngestMeasurementsSigleJobRoot(LegacyMeasurements(DateTime.UtcNow));

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
                            LoadMeasurementsPayload.Create(
                                collectionTime,
                                etlOperationId),
                            LoadMeasurementsHandler.Create(_store, _loadLogger),
                            "Load measurement summary",
                            () => _retryPolicyAbstractFactory.GetPolicy<IExponentialBackoff>());
        }

        private IDataJob<ITransformMeasurementsPayload> GetTransform(Guid etlOperationId, DateTime collectionTime)
        {
            return _dataJobFactory.DataJob<ITransformMeasurementsPayload>(
                TransformMeasurementsPayload.Create(collectionTime, etlOperationId),
                TransformMeasurementsHandler.Create(_store, _transformLogger),
                "Transform measurements");
        }

        private IDataJob<IIngestMeasurementsPayload> GetIngest(IIngestMeasurementsPayload ingestPayload)
        {
            return _dataJobFactory.DataJob(
                            ingestPayload,
                            IngestMeasurementsHandler.Create(_store, _ingestLogger),
                            "Ingest measurements");
        }
        private IDataJobRoot<IIngestMeasurementsPayload> GetSingleIngest(IngestMeasurementsPayload ingestPayload)
        {
            return _dataJobFactory.DataJobRoot<IIngestMeasurementsPayload>(
                            ingestPayload.EtlOperationId,
                            ingestPayload,
                            IngestMeasurementsHandler.Create(_store, _ingestLogger),
                            "Single job: ingest measurements");
        }

        private static LegacyMeasurement[] LegacyMeasurements(DateTime collectedAt)
        {
            return new[]
            {
                new LegacyMeasurement(
                    "boiler-outlet",
                    GenerateRandomDecimal(10.0,100.0),
                    TemperatureUnit.Celsius,
                    collectedAt.AddSeconds(-3)),
                new LegacyMeasurement(
                    "warehouse-zone-a",
                    GenerateRandomDecimal(10.0,100.0),
                    TemperatureUnit.Fahrenheit,
                    collectedAt.AddSeconds(-2)),
                new LegacyMeasurement(
                    "warehouse-zone-b",
                    GenerateRandomDecimal(10.0,100.0),
                    TemperatureUnit.Celsius,
                    collectedAt.AddSeconds(-1))
            };
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
