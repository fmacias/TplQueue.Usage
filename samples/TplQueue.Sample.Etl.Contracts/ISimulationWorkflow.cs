using System.Threading;
using System.Threading.Tasks;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Owns scheduled arrivals for one workflow across all available queues, independently of any view.</summary>
    public interface ISimulationWorkflow
    {
        /// <summary>Starts this workflow once; the token also applies to accepted jobs.</summary>
        void Start(CancellationToken cancellationToken);

        /// <summary>Releases the timer and waits for an admitted submission, without cancelling accepted jobs.</summary>
        Task StopAsync();

        /// <summary>Completes after arrivals and submissions finish, independently of job execution.</summary>
        Task Completion { get; }

        /// <summary>Returns detached delivery counters and accepted root identities aggregated across all queues.</summary>
        ScenarioDeliverySnapshot GetSnapshot();
    }
}
