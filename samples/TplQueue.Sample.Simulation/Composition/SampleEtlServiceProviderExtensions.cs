using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using TplQueue.Sample.Simulation.Handlers;
using TplQueue.Sample.Simulation.Payloads;

namespace TplQueue.Sample.Simulation.Composition
{
    /// <summary>Initializes the fixed, backend-owned sample ETL workflow.</summary>
    public static class SampleEtlServiceProviderExtensions
    {
        /// <summary>
        /// Registers the ETL payload handlers used to rehydrate cached jobs.
        /// </summary>
        /// <param name="serviceProvider">The application service provider.</param>
        public static void RegisterSampleEtlPayloadHandlers(this IServiceProvider serviceProvider)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            var api = serviceProvider.GetRequiredService<IApi>();
            api.RegisterPayloadHandler(
                IngestMeasurementsPayload.HandlerId,
                serviceProvider.GetRequiredService<IngestMeasurementsHandler>());
            api.RegisterPayloadHandler(
                TransformMeasurementsPayload.HandlerId,
                serviceProvider.GetRequiredService<TransformMeasurementsHandler>());
            api.RegisterPayloadHandler(
                LoadMeasurementsPayload.HandlerId,
                serviceProvider.GetRequiredService<LoadMeasurementsHandler>());
        }
    }
}
