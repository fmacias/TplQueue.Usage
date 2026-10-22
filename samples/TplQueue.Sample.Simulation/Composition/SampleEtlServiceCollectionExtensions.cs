using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Measurements;
using TplQueue.Sample.Simulation.Session;
using TplQueue.Sample.Simulation.Workflows.Etl;
using TplQueue.Sample.Simulation.Workflows.SingleJob;
using TplQueue.Sample.Simulation.Workflows.MultipleFifoRoots;
using TplQueue.Sample.Simulation.Graphs;
using TplQueue.Sample.Simulation.Execution;
using TplQueue.Sample.Simulation.Stores;

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

        /// <summary>Registers continuous FIFO submissions to every available queue at 100 ms intervals without an admission cap.</summary>
        /// <remarks>Delivery starts through the simulation service. Completion closes arrivals without draining jobs.</remarks>
        public static IServiceCollection AddSampleMultipleFifoRootsSimulation(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            AddSimulationServices(services);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISimulationWorkflow, MultipleFifoRootsWorkflow>());
            return services;
        }

        private static void AddSimulationServices(IServiceCollection services)
        {
            services.TryAddSingleton<IEtlExecutionDataStore, EtlExecutionDataStore>();
            services.TryAddSingleton<ISimulationGraphCatalog, SimulationGraphCatalog>();
            services.TryAddSingleton<IEtlQueueRuntime,EtlQueueRuntime>();
            services.TryAddSingleton<IMeasurementSource, SampleMeasurementSource>();
            services.TryAddSingleton<ISimulationService, SimulationService>();
        }
    }
}
