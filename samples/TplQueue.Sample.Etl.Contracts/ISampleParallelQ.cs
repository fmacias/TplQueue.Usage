using Fmacias.TplQueue.Contracts;
using System;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface ISampleParallelQ: IDisposable
    {
        IParallelQ InnerParallelQ { get; }
    }
}