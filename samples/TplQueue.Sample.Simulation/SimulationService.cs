using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Runtime;
using TplQueue.Sample.Simulation.Scenarios;

namespace TplQueue.Sample.Simulation
{
    /// <summary>Owns finite ETL scenario delivery; the host only supplies lifecycle and observers.</summary>
    internal sealed class SimulationService : ISimulationService
    {
        private readonly object _sync = new object();
        private readonly FiniteScenarioDelivery[] _deliveries;
        private bool _started, _stopped, _disposed;

        public SimulationService(ILegacyMeasurementScenario scenario, SingleJobScenario singleJob, EtlQueueRuntime runtime,
            SimulationScenarioSettings[] settings)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (singleJob == null) throw new ArgumentNullException(nameof(singleJob));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            // Composition validates and copies the settings before creating any timers.
            _deliveries = settings.Select(item => new FiniteScenarioDelivery(item,
                token => item.Kind == SimulationScenarioKind.SingleJob
                    ? singleJob.Run(item.Queue, token) : scenario.Run(item.Queue, token),
                runtime.IsActive, new ScenarioTimer())).ToArray();
            Completion = Task.WhenAll(_deliveries.Select(item => item.Completion));
        }

        public Task Completion { get; }

        public void Start(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SimulationService));
                if (_started || _stopped) throw new InvalidOperationException("The simulation can start only once.");
                cancellationToken.ThrowIfCancellationRequested();
                _started = true;
                try
                {
                    foreach (var delivery in _deliveries) delivery.Start(cancellationToken);
                }
                catch
                {
                    StopAsync();
                    throw;
                }
            }
        }

        public Task StopAsync()
        {
            lock (_sync)
            {
                _stopped = true;
                return Task.WhenAll(_deliveries.Select(item => item.StopAsync()));
            }
        }

        public IReadOnlyList<ScenarioDeliverySnapshot> GetSnapshot() =>
            Array.AsReadOnly(_deliveries.Select(item => item.GetSnapshot()).ToArray());

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                StopAsync().GetAwaiter().GetResult();
                foreach (var delivery in _deliveries) delivery.Dispose();
            }
        }
    }
}
