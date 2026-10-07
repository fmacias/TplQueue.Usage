using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.Etl.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace TplQueue.Sample.BlazorSignalR.Composition;

internal static class MeasurementEtlServiceCollectionExtensions
{

    public static IServiceCollection AddMeasurementEtlConsumer(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<IEtlQueueRuntime>();
            var parallel = runtime.GetQueue(AvailableQueue.Parallel);
            var fifo = runtime.GetQueue(AvailableQueue.FIFO);
            var cache = runtime.GetQueue(AvailableQueue.Cache);
            return new EtlQueueCatalog(new[]
            {
                new EtlQueueDescriptor(AvailableQueue.Parallel, parallel.QueueId, "parallel",
                    parallel.Name, 0, parallel.MaxParallelism),
                new EtlQueueDescriptor(AvailableQueue.FIFO, fifo.QueueId, "fifo",
                    fifo.Name, 1, fifo.MaxParallelism),
                new EtlQueueDescriptor(AvailableQueue.Cache, cache.QueueId, "cache",
                    cache.Name, 2, cache.MaxParallelism)
            });
        });
        services.AddSingleton<IEtlExecutionProjectionStore, EtlExecutionProjectionStore>();
        services.AddSingleton<EtlQueueObserver>();
        services.AddHostedService<SampleEtlDemoHostedService>();
        return services;
    }
}
