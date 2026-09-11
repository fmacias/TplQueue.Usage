using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Etl.Runtime
{
    internal sealed class EtlQueueRuntime : IDisposable, IObserver<IJobEvent>
    {
        private readonly IFifoQ _fifoQ;
        private readonly IParallelQ _parallelQ;
        private readonly ICacheQ _cacheQ;
        private readonly ILogger<EtlQueueRuntime> _logger;
        private readonly IReadOnlyList<IDisposable> _subscriptions;
        private readonly object _disposeSync = new object();
        private bool _disposed;
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations =
            new ConcurrentDictionary<Guid, CancellationTokenSource>();

        private EtlQueueRuntime(
            IFifoQ fifoQ,
            IParallelQ parallelQ,
            ICacheQ cacheQ,
            ILogger<EtlQueueRuntime> logger)
        {
            _fifoQ = fifoQ ?? throw new ArgumentNullException(nameof(fifoQ));
            _parallelQ = parallelQ ?? throw new ArgumentNullException(nameof(parallelQ));
            _cacheQ = cacheQ ?? throw new ArgumentNullException(nameof(cacheQ));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subscriptions = new[]
            {
                _fifoQ.Subscribe(this),
                _parallelQ.Subscribe(this),
                _cacheQ.Subscribe(this)
            };
        }

        public static EtlQueueRuntime Create(
            IFifoQ fifoQ,
            IParallelQ parallelQ,
            ICacheQ cacheQ,
            ILogger<EtlQueueRuntime> logger)
        {
            var runtime = new EtlQueueRuntime(fifoQ, parallelQ, cacheQ, logger);

            try
            {
                runtime.ResumePolling();
                return runtime;
            }
            catch
            {
                runtime.Dispose();
                throw;
            }
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
                switch (availableQueue)
                {
                    case AvailableQueue.FIFO:
                        _fifoQ.Enqueue(root, cancellationSource.Token);
                        break;
                    case AvailableQueue.Parallel:
                        _parallelQ.Enqueue(root, cancellationSource.Token);
                        break;
                    case AvailableQueue.Cache:
                        _cacheQ.Enqueue(root, cancellationSource.Token);
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

        public IDisposable Subscribe(IObserver<IJobEvent> observer)
        {
            ThrowIfDisposed();
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            return new CompositeSubscription(new[]
            {
                _fifoQ.Subscribe(observer),
                _parallelQ.Subscribe(observer),
                _cacheQ.Subscribe(observer)
            });
        }

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
                _fifoQ.Dispose();
                _cacheQ.Dispose();
                _parallelQ.Dispose();
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

        private void ResumePolling()
        {
            _fifoQ.ResumePolling();
            _parallelQ.ResumePolling();
            _cacheQ.ResumePolling();
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
