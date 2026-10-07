using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fmacias.TplQueue.Contracts;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Coordinates all sample workflows and observes their shared queue runtime.</summary>
    public interface ISimulationService : IObservable<IJobEvent>
    {
        /// <summary>Starts queue polling and every workflow once. The token also applies to accepted jobs.</summary>
        void Start(CancellationToken cancellationToken);

        /// <summary>Stops all arrivals and awaits pending submissions without cancelling accepted jobs.</summary>
        Task StopAsync();

        /// <summary>Completes when every workflow finishes delivering, independently of job execution.</summary>
        Task Completion { get; }

        /// <summary>Returns detached delivery snapshots for all workflows.</summary>
        IReadOnlyList<ScenarioDeliverySnapshot> GetSnapshot();
    }
}
