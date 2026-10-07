using System;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Payloads
{
    /// <summary>Carries immutable ETL metadata into the load job.</summary>
    internal sealed class LoadMeasurementsPayload : ILoadMeasurementsPayload
    {
        /// <summary>The stable handler identifier persisted with load jobs.</summary>
        public const string HandlerId = "sample.etl.load-measurements.v1";

        /// <summary>
        /// Initializes a load payload through a constructor that also supports
        /// System.Text.Json cache hydration.
        /// </summary>
        public LoadMeasurementsPayload(
            string payloadId,
            DateTime collectionTime,
            Guid etlOperationId)
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

            CollectionTime = collectionTime;
            EtlOperationId = etlOperationId;
        }

        public static LoadMeasurementsPayload Create(string payloadId,
            DateTime collectionTime,
            Guid etlOperationId)
        {
            return new LoadMeasurementsPayload(payloadId, collectionTime, etlOperationId);
        }

        /// <summary>Gets the payload instance identifier.</summary>
        public string PayloadId { get; }

        /// <summary>Gets the collection timestamp.</summary>
        public DateTime CollectionTime { get; }

        /// <summary>Gets the stable handler identifier.</summary>
        public string HandlerKey => HandlerId;

        /// <summary>Gets the ETL operation identifier.</summary>
        public Guid EtlOperationId { get; }

        /// <summary>Creates a load payload.</summary>
        public static LoadMeasurementsPayload Create(
            DateTime collectionTime,
            Guid etlOperationId)
        {
            return new LoadMeasurementsPayload(
                Guid.NewGuid().ToString("D"),
                collectionTime,
                etlOperationId);
        }
    }
}
