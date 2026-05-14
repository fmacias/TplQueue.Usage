using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test.PayloadJobs
{
    internal sealed class CelsiusClassPayload : IPayload
    {
        private static readonly Guid StableHandlerId = Guid.Parse("34343434-3434-3434-3434-343434343434");
        private readonly DateTime _collectionTime;

        public CelsiusClassPayload()
        {
            PayloadId = Guid.NewGuid().ToString();
            HandlerId = StableHandlerId;
            _collectionTime = DateTime.UtcNow;
        }

        public string PayloadId { get; init; }
        public double TemperatureCelsius { get; init; }
        public DateTime CollectionTime => _collectionTime;
        public bool Executed { get; init; }

        public Guid HandlerId { get; init; }
    }
}
