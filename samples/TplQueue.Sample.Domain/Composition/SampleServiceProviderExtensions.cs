using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Domain.Queues;
using TplQueue.Sample.Domain.Handlers;
using TplQueue.Sample.Domain.Factories;
using TplQueue.Sample.Domain.Cache;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Domain.Stores;
using Microsoft.Extensions.Logging;
using Fmacias.TplQueue.Cache.MemCache;

namespace TplQueue.Sample.Domain.Composition
{
    /// <summary>Initializes the fixed, backend-owned sample ETL workflow.</summary>
    public static class SampleServiceProviderExtensions
    {
        /// <summary>Registers Domain services without starting simulation arrivals.</summary>
        public static IServiceCollection AddSampleDomain(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddTransient<ICacheTypeResolver, CacheTypeResolver>();
            services.AddTransient<ISampleCache>(CreateMemCache);
            services.AddTransient<ISampleParallelQ, SampleParallelQ>();
            services.AddTransient<ISampleFifoQ, SampleFifoQ>();
            services.AddTransient<ISampleCacheQ, SampleCacheQ>();
            services.AddSingleton<IEtlExecutionDataStore, EtlExecutionDataStore>();
            services.AddSingleton<ISampleJobFactory, SampleJobFactory>();
            return services;
        }


        /// <summary>
        /// Registers the ETL payload handlers used to rehydrate cached jobs.
        /// </summary>
        /// <param name="serviceProvider">The application service provider.</param>
        public static void RegisterSampleEtlPayloadHandlers(this IServiceProvider serviceProvider)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
            var api = serviceProvider.GetRequiredService<IApi>();
            var store = serviceProvider.GetRequiredService<IEtlExecutionDataStore>();
         
            var ingestHandler = IngestMeasurementsHandler.Create(store, serviceProvider.GetRequiredService<ILogger<IngestMeasurementsHandler>>());
            var transformHandler = TransformMeasurementsHandler.Create(store, serviceProvider.GetRequiredService<ILogger<TransformMeasurementsHandler>>());
            var loadHandler = LoadMeasurementsHandler.Create(store, serviceProvider.GetRequiredService<ILogger<LoadMeasurementsHandler>>());

            api.RegisterPayloadHandler(
                IngestMeasurementsPayload.HandlerId,
                ingestHandler);

            api.RegisterPayloadHandler(
                TransformMeasurementsPayload.HandlerId,
                transformHandler);

            api.RegisterPayloadHandler(
                LoadMeasurementsPayload.HandlerId,
                loadHandler);
        }
 
        private static ISampleCache CreateMemCache(IServiceProvider serviceProvider)
        {
            var api = serviceProvider.GetRequiredService<IApi>();
            var serializer = serviceProvider.GetRequiredService<ISystemTextJsonUniversalSerializer>();
            var typeResolver = serviceProvider.GetRequiredService<ICacheTypeResolver>();

            return SampleCache.Create(
                api.Cache<IMemCache>(
                    MemCacheFactory.Create(),
                    serializer,
                    typeResolver));
        }
    }
}
