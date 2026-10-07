using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Queues
{
    /// <summary>Owns the configured sample cache queue.</summary>
    internal sealed class SampleCacheQ : ISampleCacheQ
    {
        private int _disposed;

        public SampleCacheQ(IQFactoryAdapter queueFactory, ILogger<ICacheQ> logger,
            ISampleCache sampleCache)
        {
            if (queueFactory == null) throw new ArgumentNullException(nameof(queueFactory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (sampleCache == null) throw new ArgumentNullException(nameof(sampleCache));
            InnerCacheQ = queueFactory.CacheQ(() => sampleCache.Cache, logger,
                queueFactory.Parallel("CacheQ", logger));
        }

        public ICacheQ InnerCacheQ { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) InnerCacheQ.Dispose();
        }
    }
}
