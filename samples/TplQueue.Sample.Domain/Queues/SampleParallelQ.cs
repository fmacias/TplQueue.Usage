using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Queues
{
    /// <summary>Owns the configured sample parallel queue.</summary>
    internal sealed class SampleParallelQ : ISampleParallelQ
    {
        private int _disposed;

        public SampleParallelQ(IQFactoryAdapter queueFactory, ILogger<IParallelQ> logger)
        {
            if (queueFactory == null) throw new ArgumentNullException(nameof(queueFactory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            InnerParallelQ = queueFactory.Parallel("ParallelQ", logger);
        }

        public IParallelQ InnerParallelQ { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) InnerParallelQ.Dispose();
        }
    }
}
