using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Consumers.JobEventTransport
{
    internal sealed class RecordingTransportObserver : IObserver<IJobEvent>
    {
        private readonly JobEventTransportProjector _projector;
        private readonly object _sync = new();
        private readonly List<JobEventTransportDto> _events = new();

        public RecordingTransportObserver(JobEventTransportProjector projector)
        {
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        }

        public Exception? ObserverError { get; private set; }

        public IReadOnlyList<JobEventTransportDto> Snapshot()
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            ObserverError = error;
        }

        public void OnNext(IJobEvent value)
        {
            var dto = _projector.Project(value);

            lock (_sync)
            {
                _events.Add(dto);
            }
        }
    }
}
