using System;
using System.Collections.Generic;
using System.Linq;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Detached delivery evidence; it does not substitute for runtime job lifecycle events.</summary>
    public sealed class ScenarioDeliverySnapshot
    {
        public ScenarioDeliverySnapshot(string scenarioId, int ticks, int skippedTicks,
            int failedTicks, string? lastError, IEnumerable<Guid> rootIds)
        {
            ScenarioId = scenarioId;
            Ticks = ticks;
            SkippedTicks = skippedTicks;
            FailedTicks = failedTicks;
            LastError = lastError;
            RootIds = Array.AsReadOnly(rootIds.ToArray());
        }

        public string ScenarioId { get; }
        public int Ticks { get; }
        /// <summary>Gets overlapping timer ticks skipped while the preceding submission callback was still busy.</summary>
        public int SkippedTicks { get; }
        /// <summary>Gets ticks with at least one submission failure. Successful submissions to other queues retain their roots.</summary>
        public int FailedTicks { get; }
        public string? LastError { get; }
        public IReadOnlyList<Guid> RootIds { get; }
    }
}
