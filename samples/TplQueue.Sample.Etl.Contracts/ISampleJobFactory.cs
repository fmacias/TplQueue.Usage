
using Fmacias.TplQueue.Contracts;
using System.Collections.Generic;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface ISampleJobFactory
    {
        IDataJobRoot<IIngestMeasurementsPayload> IngestMeasurementsSigleJobRoot();
        IDataJobRoot<ILoadMeasurementsPayload> LoadMeasurementsJobRoot();

        /// <summary>Creates an independent ingest root from a snapshot of supplied measurements.</summary>
        IDataJobRoot<IIngestMeasurementsPayload> IngestMeasurementsSigleJobRoot(
            IReadOnlyList<LegacyMeasurement> measurements);

        /// <summary>Composes Ingest, Transform and Load from a snapshot of supplied measurements.</summary>
        IDataJobRoot<ILoadMeasurementsPayload> LoadMeasurementsJobRoot(
            IReadOnlyList<LegacyMeasurement> measurements);
    }
}
