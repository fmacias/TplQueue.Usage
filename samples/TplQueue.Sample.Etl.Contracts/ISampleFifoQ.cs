using Fmacias.TplQueue.Contracts;
using System;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface ISampleFifoQ: IDisposable
    {
        IFifoQ InnerFifoQ { get; }
    }
}