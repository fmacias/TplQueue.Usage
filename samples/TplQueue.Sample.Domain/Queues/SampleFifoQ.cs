using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Queues
{
    /// <summary>Owns the configured sample fifo queue.</summary>
    internal sealed class SampleFifoQ : ISampleFifoQ
    {
        private int _disposed;

        public SampleFifoQ(IQFactoryAdapter queueFactory, ILogger<IFifoQ> logger)
        {
            if (queueFactory == null) throw new ArgumentNullException(nameof(queueFactory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            InnerFifoQ = queueFactory.Fifo("FifoQ", logger);
        }

        public IFifoQ InnerFifoQ { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) InnerFifoQ.Dispose();
        }
    }
}
