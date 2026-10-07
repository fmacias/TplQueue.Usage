using Fmacias.TplQueue.Contracts;
using System;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface ISampleCacheQ: IDisposable
    {
        ICacheQ InnerCacheQ { get; }
    }
}