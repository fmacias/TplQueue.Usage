using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Measurements;
using TplQueue.Sample.Simulation.Runtime;
using TplQueue.Sample.Simulation.Session;
using TplQueue.Sample.Simulation.Workflows.Etl;
using TplQueue.Sample.Simulation.Workflows.SingleJob;

namespace TplQueue.Sample.Simulation.Composition
{
    /// <summary>Registers fixed workflows using contracts supplied by the application.</summary>
    public static class SampleEtlServiceCollectionExtensions
    {
        /// <summary>Registers continuous ETL arrivals on each queue without starting delivery.</summary>
        public static IServiceCollection AddSampleEtlWorkflow(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            AddSimulationServices(services);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISimulationWorkflow, EtlSimulationWorkflow>());
            return services;
        }

        /// <summary>Registers continuous single-job arrivals on each queue without starting delivery.</summary>
        public static IServiceCollection AddSampleSingleJobSimulation(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            AddSimulationServices(services);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISimulationWorkflow, SingleJobWorkflow>());
            return services;
        }

        private static void AddSimulationServices(IServiceCollection services)
        {
            services.TryAddSingleton<ISimulationGraphCatalog, SimulationGraphCatalog>();
            services.TryAddSingleton<IEtlQueueRuntime,EtlQueueRuntime>();
            services.TryAddSingleton<IMeasurementSource, SampleMeasurementSource>();
            services.TryAddSingleton<ISimulationService, SimulationService>();
        }
    }
}
