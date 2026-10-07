using System;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Payloads
{
    /// <summary>Carries immutable ETL metadata into the transform job.</summary>
    internal sealed class TransformMeasurementsPayload : ITransformMeasurementsPayload
    {
        /// <summary>The stable handler identifier persisted with transform jobs.</summary>
        public const string HandlerId = "sample.etl.transform-measurements.v1";

        /// <summary>
        /// Initializes a transform payload through a constructor that also supports
        /// System.Text.Json cache hydration.
        /// </summary>
        public TransformMeasurementsPayload(
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

        public static TransformMeasurementsPayload Create(string payloadId, DateTime collectionTime, Guid etlOperationId)
        {
            return new TransformMeasurementsPayload(payloadId, collectionTime, etlOperationId);
        }


        /// <summary>Gets the payload instance identifier.</summary>
        public string PayloadId { get; }

        /// <summary>Gets the collection timestamp.</summary>
        public DateTime CollectionTime { get; }

        /// <summary>Gets the stable handler identifier.</summary>
        public string HandlerKey => HandlerId;

        /// <summary>Gets the ETL operation identifier.</summary>
        public Guid EtlOperationId { get; }

        /// <summary>Creates a transform payload.</summary>
        public static TransformMeasurementsPayload Create(
            DateTime collectionTime,
            Guid etlOperationId)
        {
            return TransformMeasurementsPayload.Create(Guid.NewGuid().ToString("D"),
                collectionTime, etlOperationId);
        }
    }
}
