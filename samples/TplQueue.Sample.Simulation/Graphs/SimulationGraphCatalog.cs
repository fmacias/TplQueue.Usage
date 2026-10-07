using Fmacias.TplQueue.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Runtime
{
    /// <summary>Retains only IDs from composed graphs for the lifetime of the simulation session.</summary>
    internal sealed class SimulationGraphCatalog : ISimulationGraphCatalog
    {
        private readonly object _sync = new object();
        private readonly Dictionary<Guid, HashSet<Guid>> _rootsByJob = new Dictionary<Guid, HashSet<Guid>>();

        /// <summary>Captures membership before enqueue can add FIFO ordering edges or publish events.</summary>
        public void Register(IJobInfo root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var visited = new HashSet<Guid>();
            var pending = new Stack<IJobInfo>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var job = pending.Pop();
                if (!visited.Add(job.Id)) continue;
                foreach (var dependency in job.Dependencies ?? Array.Empty<IJobInfo>())
                    pending.Push(dependency);
            }

            lock (_sync)
            {
                foreach (var id in visited)
                {
                    if (!_rootsByJob.TryGetValue(id, out var roots))
                        _rootsByJob.Add(id, roots = new HashSet<Guid>());
                    roots.Add(root.Id);
                }
            }
        }

        public IReadOnlyList<Guid> GetRootJobIds(Guid jobId)
        {
            lock (_sync)
            {
                return _rootsByJob.TryGetValue(jobId, out var roots)
                    ? Array.AsReadOnly(roots.OrderBy(id => id).ToArray())
                    : Array.Empty<Guid>();
            }
        }
    }
}
