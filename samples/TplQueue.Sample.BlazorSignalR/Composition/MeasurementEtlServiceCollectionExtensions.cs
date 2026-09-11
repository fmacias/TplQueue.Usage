using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;
using Fmacias.TplQueue.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Composition;

internal static class MeasurementEtlServiceCollectionExtensions
{

    public static IServiceCollection AddMeasurementEtlConsumer(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<ILegacyMeasurementCollector, LegacyMeasurementCollector>();
        services.AddSingleton<ILegacyMeasurementScenario, LegacyMeasurementScenario>();
        services.AddSingleton(sp => new EtlQueueCatalog(new[]
        {
            new EtlQueueDescriptor(
                AvailableQueue.Parallel,
                sp.GetRequiredService<IParallelQ>().QueueId,
                "parallel",
                "ParallelQ",
                0),
            new EtlQueueDescriptor(
                AvailableQueue.FIFO,
                sp.GetRequiredService<IFifoQ>().QueueId,
                "fifo",
                "FifoQ",
                1),
            new EtlQueueDescriptor(
                AvailableQueue.Cache,
                sp.GetRequiredService<ICacheQ>().QueueId,
                "cache",
                "CacheQ",
                2)
        }));
        services.AddSingleton<IEtlExecutionProjectionStore, EtlExecutionProjectionStore>();
        services.AddSingleton<EtlQueueObserver>();
        services.AddHostedService<SampleEtlDemoHostedService>();
        return services;
    }
}
