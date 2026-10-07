using Fmacias.TplQueue.Contracts;

namespace TplQueue.Sample.Etl.Contracts
{
    public interface ISampleCache
    {
        IMemCache Cache { get; }
    }
}