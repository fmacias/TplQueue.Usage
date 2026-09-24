using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Handlers;
using TplQueue.Sample.Simulation.Runtime;
using TplQueue.Sample.Simulation.Scenarios;

namespace TplQueue.Sample.Simulation.Composition
{
    /// <summary>Registers the fixed, backend-owned sample ETL workflow.</summary>
    public static class SampleEtlServiceCollectionExtensions
    {
        /// <summary>Registers the workflow and the default finite simulation (two roots per queue).</summary>
        public static IServiceCollection AddSampleEtlWorkflow(this IServiceCollection services)
            => AddSampleEtlWorkflow(services, new[]
            {
                DefaultScenario("etl-parallel", AvailableQueue.Parallel),
                DefaultScenario("etl-fifo", AvailableQueue.FIFO),
                DefaultScenario("etl-cache", AvailableQueue.Cache)
            });

        /// <summary>Registers finite scenarios; resolving the service does not start arrivals.</summary>
        public static IServiceCollection AddSampleEtlWorkflow(this IServiceCollection services,
            IEnumerable<SimulationScenarioSettings> scenarios)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (scenarios == null) throw new ArgumentNullException(nameof(scenarios));
            var settings = scenarios.ToArray();
            if (settings.Length == 0 || settings.Any(item => item == null))
                throw new ArgumentException("At least one non-null scenario is required.", nameof(scenarios));
            if (settings.Select(item => item.ScenarioId).Distinct(StringComparer.Ordinal).Count() != settings.Length)
                throw new ArgumentException("Scenario IDs must be unique.", nameof(scenarios));

            services.TryAddSingleton<EtlExecutionDataStore>();
            services.TryAddSingleton<IngestMeasurementsHandler>();
            services.TryAddSingleton<TransformMeasurementsHandler>();
            services.TryAddSingleton<LoadMeasurementsHandler>();
            services.TryAddSingleton<EtlPayloadTypeResolver>();
            services.TryAddSingleton<ITypeResolver>(
                sp => sp.GetRequiredService<EtlPayloadTypeResolver>());
            services.TryAddTransient<IMemCache>(CreateMemoryCache);
            services.TryAddSingleton<IFifoQ>(CreateFifoQueue);
            services.TryAddSingleton<IParallelQ>(CreateParallelQueue);
            services.TryAddSingleton<ICacheQ>(CreateCacheQueue);
            services.TryAddSingleton<EtlQueueRuntime>(sp => EtlQueueRuntime.Create(
                sp.GetRequiredService<IFifoQ>(),
                sp.GetRequiredService<IParallelQ>(),
                sp.GetRequiredService<ICacheQ>(),
                sp.GetRequiredService<ILogger<EtlQueueRuntime>>()));
            services.TryAddSingleton<IEtlWorkflow, EtlWorkflow>();
            services.TryAddSingleton<ILegacyMeasurementCollector, LegacyMeasurementCollector>();
            services.TryAddSingleton<ILegacyMeasurementScenario, LegacyMeasurementScenario>();
            services.TryAddSingleton<ISimulationService>(sp => new SimulationService(
                sp.GetRequiredService<ILegacyMeasurementScenario>(),
                sp.GetRequiredService<EtlQueueRuntime>(), settings));
            return services;
        }

        private static SimulationScenarioSettings DefaultScenario(string id, AvailableQueue queue) =>
            new SimulationScenarioSettings(id, queue, TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1), 1, 2, 2);
        private static IParallelQ CreateParallelQueue(IServiceProvider serviceProvider)
        {
            var factory = serviceProvider.GetRequiredService<IQFactoryAdapter>();
            var logger = serviceProvider.GetRequiredService<ILogger<IParallelQ>>();

            return factory.Parallel("ParallelQ", logger);
        }

        private static IFifoQ CreateFifoQueue(IServiceProvider serviceProvider)
        {
            var factory = serviceProvider.GetRequiredService<IQFactoryAdapter>();
            var logger = serviceProvider.GetRequiredService<ILogger<IFifoQ>>();

            return factory.Fifo("FifoQ", logger);
        }

        private static IMemCache CreateMemoryCache(IServiceProvider serviceProvider)
        {
            var api = serviceProvider.GetRequiredService<IApi>();
            var serializer = serviceProvider.GetRequiredService<ISystemTextJsonUniversalSerializer>();
            var typeResolver = serviceProvider.GetRequiredService<EtlPayloadTypeResolver>();

            return api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                serializer,
                typeResolver);
        }
        private static ICacheQ CreateCacheQueue(IServiceProvider serviceProvider)
        {
            var factory = serviceProvider.GetRequiredService<IQFactoryAdapter>();
            var innerLogger = serviceProvider.GetRequiredService<ILogger<IParallelQ>>();
            var cacheLogger = serviceProvider.GetRequiredService<ILogger<ICacheQ>>();
            var innerQueue = factory.Parallel("CacheQ", innerLogger);

            return factory.CacheQ(
                () => serviceProvider.GetRequiredService<IMemCache>(),
                cacheLogger,
                innerQueue);
        }
    }
}
