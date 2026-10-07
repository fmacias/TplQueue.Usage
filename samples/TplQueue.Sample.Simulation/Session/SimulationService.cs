using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fmacias.TplQueue.Contracts;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Simulation.Session
{
    /// <summary>Coordinates the shared runtime and the lifecycle of all registered workflows.</summary>
    internal sealed class SimulationService : ISimulationService
    {
        private readonly object _sync = new object();
        private readonly ISimulationWorkflow[] _workflows;
        private readonly IEtlQueueRuntime _runtime;
        private bool _started;
        private Task? _stopTask;

        public SimulationService(IEnumerable<ISimulationWorkflow> workflows, IEtlQueueRuntime runtime)
        {
            if (workflows == null) throw new ArgumentNullException(nameof(workflows));
            _workflows = workflows.ToArray();
            if (_workflows.Length == 0 || _workflows.Any(workflow => workflow == null))
                throw new ArgumentException("At least one non-null workflow is required.", nameof(workflows));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Completion = Task.WhenAll(_workflows.Select(workflow => workflow.Completion));
        }

        public Task Completion { get; }

        public void Start(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_started || _stopTask != null)
                    throw new InvalidOperationException("The simulation can start only once.");
                cancellationToken.ThrowIfCancellationRequested();
                _started = true;
                try
                {
                    _runtime.ResumePolling();
                    foreach (var workflow in _workflows) workflow.Start(cancellationToken);
                }
                catch
                {
                    // Begin cleanup immediately; StopAsync lets the host await every pending submission.
                    _ = StopAsync();
                    throw;
                }
            }
        }

        public Task StopAsync()
        {
            lock (_sync)
                return _stopTask ??= Task.WhenAll(_workflows.Select(StopWorkflowAsync));
        }

        public IDisposable Subscribe(IObserver<IJobEvent> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            return _runtime.Subscribe(observer);
        }

        public IReadOnlyList<ScenarioDeliverySnapshot> GetSnapshot() =>
            Array.AsReadOnly(_workflows.Select(workflow => workflow.GetSnapshot()).ToArray());

        private static async Task StopWorkflowAsync(ISimulationWorkflow workflow)
        {
            // Capture synchronous failures so every workflow still receives its stop request.
            await workflow.StopAsync().ConfigureAwait(false);
        }
    }
}
