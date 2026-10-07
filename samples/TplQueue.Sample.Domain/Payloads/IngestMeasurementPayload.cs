using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Payloads
{
    /// <summary>Carries immutable measurement snapshots into the ingest job.</summary>
    internal sealed class IngestMeasurementsPayload : IIngestMeasurementsPayload
    {
        /// <summary>The stable handler identifier persisted with ingest jobs.</summary>
        public const string HandlerId = "sample.etl.ingest-measurements.v1";

        /// <summary>
        /// Initializes an ingest payload through a constructor that also supports
        /// System.Text.Json cache hydration.
        /// </summary>
        public IngestMeasurementsPayload(
            string payloadId,
            DateTime collectionTime,
            Guid etlOperationId,
            IReadOnlyList<MeasurementSnapshot> measurements)
        {
            PayloadId = string.IsNullOrWhiteSpace(payloadId)
                ? throw new ArgumentException("A payload identifier is required.", nameof(payloadId))
                : payloadId;

            if (etlOperationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An ETL operation identifier is required.",
                    nameof(etlOperationId));
            }

            if (measurements == null)
            {
                throw new ArgumentNullException(nameof(measurements));
            }

            if (measurements.Count == 0)
            {
                throw new ArgumentException(
                    "At least one measurement is required.",
                    nameof(measurements));
            }

            var snapshots = new List<MeasurementSnapshot>(measurements.Count);
            foreach (var measurement in measurements)
            {
                if (measurement == null)
                {
                    throw new ArgumentException(
                        "Measurements cannot contain null elements.",
                        nameof(measurements));
                }
                snapshots.Add(measurement);
            }

            CollectionTime = collectionTime;
            EtlOperationId = etlOperationId;
            Measurements = new ReadOnlyCollection<MeasurementSnapshot>(snapshots);
        }

        /// <summary>
        /// Creates an ingest payload by copying consumer-owned measurement values.
        /// </summary>
        public static IngestMeasurementsPayload Create(
            IReadOnlyList<LegacyMeasurement> measurements,
            Guid etlOperationId)
        {
            if (measurements == null)
            {
                throw new ArgumentNullException(nameof(measurements));
            }

            if (measurements.Count == 0)
            {
                throw new ArgumentException(
                    "At least one measurement is required.",
                    nameof(measurements));
            }

            if (etlOperationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "An ETL operation identifier is required.",
                    nameof(etlOperationId));
            }

            var snapshots = new List<MeasurementSnapshot>(measurements.Count);

            foreach (var measurement in measurements)
            {
                if (measurement == null)
                {
                    throw new ArgumentException(
                        "Measurements cannot contain null elements.",
                        nameof(measurements));
                }

                snapshots.Add(new MeasurementSnapshot(
                    measurement.SensorCode,
                    measurement.Value,
                    measurement.Unit,
                    measurement.ObservedAtUtc));
            }

            var collectionTime = snapshots.Min(measurement => measurement.ObservedAtUtc);
            return new IngestMeasurementsPayload(
                Guid.NewGuid().ToString("D"),
                collectionTime,
                etlOperationId,
                snapshots);
        }


        /// <summary>Gets the payload instance identifier.</summary>
        public string PayloadId { get; }

        /// <summary>Gets the collection timestamp.</summary>
        public DateTime CollectionTime { get; }

        /// <summary>Gets the stable handler identifier.</summary>
        public string HandlerKey => HandlerId;

        /// <summary>Gets the ETL operation identifier.</summary>
        public Guid EtlOperationId { get; }

        /// <summary>Gets the immutable measurement snapshots.</summary>
        public IReadOnlyList<MeasurementSnapshot> Measurements { get; }
    }
}
