using Fmacias.TplQueue.Contracts;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Cache
{
    internal sealed class SampleCache : ISampleCache
    {
        private readonly IMemCache _cache;
        private SampleCache(IMemCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }
        public static SampleCache Create(IMemCache cache)
        {
            return new SampleCache(cache);
        }
        public IMemCache Cache => _cache;
    }
}
