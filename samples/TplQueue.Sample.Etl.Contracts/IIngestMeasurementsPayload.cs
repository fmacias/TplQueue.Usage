using Fmacias.TplQueue.Contracts;
using System;
using System.Collections.Generic;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface IIngestMeasurementsPayload: IPayload
    {
        Guid EtlOperationId { get; }
        IReadOnlyList<MeasurementSnapshot> Measurements { get; }
    }
}