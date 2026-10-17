using Fmacias.TplQueue.Contracts;
using System;
using System.Threading;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Provides access to the shared queues held by the simulation runtime.</summary>
    public interface IEtlQueueRuntime : IObservable<IJobEvent>
    {
        /// <summary>Returns the existing queue for inspection; its lifetime belongs to the runtime.</summary>
        IQ GetQueue(AvailableQueue queue);

        /// <summary>Resumes polling on all held queues after application observers have been attached.</summary>
        void ResumePolling();

        /// <summary>Submits a fresh root using the selected queue's normal enqueue operation.</summary>
        void Enqueue<TPayload>(AvailableQueue availableQueue, IDataJobRoot<TPayload> root, CancellationToken cancellationToken)
            where TPayload : IPayload;

        /// <summary>Submits a fresh root using the selected queue's FIFO enqueue operation.</summary>
        /// <remarks>
        /// Uses EnqueueFifo on ParallelQ and CacheQ, and Enqueue on the inherently serial FifoQ.
        /// Captures composed graph membership before submission and links execution to the supplied token.
        /// </remarks>
        void EnqueueFifo<TPayload>(AvailableQueue availableQueue, IDataJobRoot<TPayload> root, CancellationToken cancellationToken)
            where TPayload : IPayload;

        bool IsActive(Guid rootJobId);
    }
}
