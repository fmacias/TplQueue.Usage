using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Runtime
{
    internal sealed class EtlQueueRuntime : IEtlQueueRuntime, IDisposable, IObserver<IJobEvent>
    {
        private readonly ISampleFifoQ _sampleFifo;
        private readonly ISampleParallelQ _sampleParallel;
        private readonly ISampleCacheQ _sampleCacheQ;
        private readonly ILogger<EtlQueueRuntime> _logger;
        private readonly ISimulationGraphCatalog _graphs;
        private readonly IReadOnlyList<IDisposable> _subscriptions;
        private readonly object _disposeSync = new object();
        private bool _disposed;
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations =
            new ConcurrentDictionary<Guid, CancellationTokenSource>();

        public EtlQueueRuntime(
            ISampleFifoQ fifoQ,
            ISampleParallelQ parallelQ,
            ISampleCacheQ cacheQ,
            ILogger<EtlQueueRuntime> logger,
            ISimulationGraphCatalog graphs)
        {
            _sampleFifo = fifoQ ?? throw new ArgumentNullException(nameof(fifoQ));
            _sampleParallel = parallelQ ?? throw new ArgumentNullException(nameof(parallelQ));
            _sampleCacheQ = cacheQ ?? throw new ArgumentNullException(nameof(cacheQ));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
            _subscriptions = new[]
            {
                _sampleFifo.InnerFifoQ.Subscribe(this),
                _sampleParallel.InnerParallelQ.Subscribe(this),
                _sampleCacheQ.InnerCacheQ.Subscribe(this)
            };
        }

        public void Enqueue<TPayload>(
            AvailableQueue availableQueue,
            IDataJobRoot<TPayload> root,
            CancellationToken cancellationToken)
            where TPayload : IPayload
        {
            ThrowIfDisposed();
            if (root == null) throw new ArgumentNullException(nameof(root));
            cancellationToken.ThrowIfCancellationRequested();

            var cancellationSource = CreateRootCancellationSource(root, cancellationToken);

            try
            {
                // Composition identity is available even to synchronous enqueue observers.
                // Retain it on submission failure: a queue may already have published events.
                _graphs.Register(root);
                switch (availableQueue)
                {
                    case AvailableQueue.FIFO:
                        _sampleFifo.InnerFifoQ.Enqueue(root, cancellationSource.Token);
                        break;
                    case AvailableQueue.Parallel:
                        _sampleParallel.InnerParallelQ.Enqueue(root, cancellationSource.Token);
                        break;
                    case AvailableQueue.Cache:
                        _sampleCacheQ.InnerCacheQ.Enqueue(root, cancellationSource.Token);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Queue type ({availableQueue}) not recognized.");
                }
            }
            catch
            {
                ReleaseCancellation(root.Id);
                throw;
            }
        }

        /// <summary>Returns the same queue instance used for workflow submission.</summary>
        public IQ GetQueue(AvailableQueue queue)
        {
            ThrowIfDisposed();
            switch (queue)
            {
                case AvailableQueue.FIFO: return _sampleFifo.InnerFifoQ;
                case AvailableQueue.Parallel: return _sampleParallel.InnerParallelQ;
                case AvailableQueue.Cache: return _sampleCacheQ.InnerCacheQ;
                default: throw new ArgumentOutOfRangeException(nameof(queue));
            }
        }

        public IDisposable Subscribe(IObserver<IJobEvent> observer)
        {
            ThrowIfDisposed();
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            return new CompositeSubscription(new[]
            {
                _sampleFifo.InnerFifoQ.Subscribe(observer),
                _sampleParallel.InnerParallelQ.Subscribe(observer),
                _sampleCacheQ.InnerCacheQ.Subscribe(observer)
            });
        }

        /// <summary>Includes queued roots until an observed terminal outcome releases their registration.</summary>
        public bool IsActive(Guid rootJobId) => _cancellations.ContainsKey(rootJobId);

        public bool Cancel(Guid rootJobId)
        {
            ThrowIfDisposed();

            if (!_cancellations.TryGetValue(rootJobId, out var cancellation))
            {
                return false;
            }

            try
            {
                cancellation.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            lock (_disposeSync)
            {
                if (_disposed) return;
                _disposed = true;

                foreach (var subscription in _subscriptions)
                {
                    subscription.Dispose();
                }

                DisposeRelatedCancellations();
                _sampleFifo.Dispose();
                _sampleCacheQ.Dispose();
                _sampleParallel.Dispose();
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            _logger.LogError(error, "A TplQueue ETL queue observer terminated with an error.");
        }

        public void OnNext(IJobEvent value)
        {
            if (value?.JobInfo == null)
            {
                return;
            }

            if (value.Status == JobEventStatus.RootSuccessed ||
                value.Status == JobEventStatus.Failed ||
                value.Status == JobEventStatus.Canceled)
            {
                ReleaseCancellation(value.JobInfo.Id);
            }
        }

        /// <summary>Starts queue polling after the application has attached its observers.</summary>
        public void ResumePolling()
        {
            ThrowIfDisposed();
            _sampleFifo.InnerFifoQ.ResumePolling();
            _sampleParallel.InnerParallelQ.ResumePolling();
            _sampleCacheQ.InnerCacheQ.ResumePolling();
        }

        private CancellationTokenSource CreateRootCancellationSource(
            IDataJobRoot root,
            CancellationToken cancellationToken)
        {
            var cancellationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (!_cancellations.TryAdd(root.Id, cancellationSource))
            {
                cancellationSource.Dispose();
                throw new InvalidOperationException(
                    $"Cancellation state for root job '{root.Id}' already exists.");
            }

            return cancellationSource;
        }

        private void ReleaseCancellation(Guid rootJobId)
        {
            if (_cancellations.TryRemove(rootJobId, out var cancellation))
            {
                cancellation.Dispose();
            }
        }

        private void DisposeRelatedCancellations()
        {
            foreach (var rootJobId in _cancellations.Keys)
            {
                if (_cancellations.TryRemove(rootJobId, out var cancellation))
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    cancellation.Dispose();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EtlQueueRuntime));
            }
        }
        private sealed class CompositeSubscription : IDisposable
        {
            private IReadOnlyList<IDisposable>? _subscriptions;

            public CompositeSubscription(IEnumerable<IDisposable> subscriptions)
            {
                _subscriptions = subscriptions?.ToArray()
                    ?? throw new ArgumentNullException(nameof(subscriptions));
            }

            public void Dispose()
            {
                var subscriptions = Interlocked.Exchange(ref _subscriptions, null);
                if (subscriptions == null) return;

                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
            }
        }
    }
}
