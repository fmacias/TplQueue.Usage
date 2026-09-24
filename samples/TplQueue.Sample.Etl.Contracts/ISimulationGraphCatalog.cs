using System;
using System.Collections.Generic;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Composition membership captured before submission, independent of execution outcomes.</summary>
    public interface ISimulationGraphCatalog
    {
        /// <summary>
        /// Returns detached root IDs for a job, or an empty list when unknown.
        /// Each root ID identifies one composed simulation run. Membership does not imply acceptance, execution or queue ownership.
        /// </summary>
        IReadOnlyList<Guid> GetRootJobIds(Guid jobId);
    }
}
