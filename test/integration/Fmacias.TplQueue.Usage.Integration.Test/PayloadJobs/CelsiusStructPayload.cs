using Fmacias.TplQueue.Contracts;
namespace Fmacias.TplQueue.Integration.Test.PayloadJobs
{
    internal readonly struct CelsiusStructPayload : IPayload
    {
        private static readonly Guid StableHandlerId = Guid.Parse("12121212-1212-1212-1212-121212121212");

        public CelsiusStructPayload()
        {
            PayloadId = Guid.NewGuid().ToString();
            HandlerId = StableHandlerId;
            CollectionTime = DateTime.UtcNow;
        }

        public string PayloadId { get; init; }
        public double Temperature { get; init; }
        public DateTime CollectionTime { get; init; }
        public bool Executed { get; init; }

        public Guid HandlerId { get; init; }
    }
}
