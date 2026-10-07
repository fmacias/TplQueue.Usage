using Fmacias.TplQueue.Contracts;
using System;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface ILoadMeasurementsPayload: IPayload
    {
        Guid EtlOperationId { get; }
    }
}