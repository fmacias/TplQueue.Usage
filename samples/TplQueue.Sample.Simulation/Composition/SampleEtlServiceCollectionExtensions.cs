using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Handlers;
using TplQueue.Sample.Simulation.Runtime;

namespace TplQueue.Sample.Simulation.Composition
{
    /// <summary>Registers the fixed, backend-owned sample ETL workflow.</summary>
    public static class SampleEtlServiceCollectionExtensions
    {
        public static IServiceCollection AddSampleEtlWorkflow(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

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
            return services;
        }
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
