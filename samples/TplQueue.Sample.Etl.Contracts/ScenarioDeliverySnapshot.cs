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
        /// <summary>Gets ticks skipped while a submission was busy or a full batch exceeded admission capacity.</summary>
        public int SkippedTicks { get; }
        /// <summary>Gets submission/admission failures. A partially submitted batch retains its accepted roots.</summary>
        public int FailedTicks { get; }
        public string? LastError { get; }
        public IReadOnlyList<Guid> RootIds { get; }
    }
}
