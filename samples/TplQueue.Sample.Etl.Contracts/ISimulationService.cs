using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Owns one finite simulation session, independently of browser connections.</summary>
    public interface ISimulationService : IDisposable
    {
        /// <summary>Starts arrivals once. The token also cancels submitted ETL graphs.</summary>
        void Start(CancellationToken cancellationToken);
        /// <summary>Closes admission and waits for in-flight submissions, without draining or cancelling jobs.</summary>
        Task StopAsync();
        /// <summary>Completes after all finite arrivals and submissions finish, not after graph execution.</summary>
        Task Completion { get; }
        /// <summary>Returns detached delivery counters and accepted root identities.</summary>
        IReadOnlyList<ScenarioDeliverySnapshot> GetSnapshot();
    }
}
