using TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;
using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.Etl.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace TplQueue.Sample.BlazorSignalR.Composition;

internal static class MeasurementEtlServiceCollectionExtensions
{

    public static IServiceCollection AddMeasurementEtlConsumer(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<IEtlQueueRuntime>();
            var parallel = runtime.GetQueue(AvailableQueue.Parallel);
            var fifo = runtime.GetQueue(AvailableQueue.FIFO);
            var cache = runtime.GetQueue(AvailableQueue.Cache);
            return new JobMonitorQueueCatalog(new[]
            {
                new JobMonitorQueue(parallel.QueueId, "parallel", parallel.Name, parallel.MaxParallelism),
                new JobMonitorQueue(fifo.QueueId, "fifo", fifo.Name, fifo.MaxParallelism),
                new JobMonitorQueue(cache.QueueId, "cache", cache.Name, cache.MaxParallelism)
            });
        });
        var retention = services.AddOptions<JobMonitorRetentionOptions>();
        if (configuration != null) retention.Bind(configuration.GetSection("JobMonitor"));
        retention.Validate(options => options.MaximumEvents > 0, "JobMonitor:MaximumEvents must be positive.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<JobMonitorRetentionOptions>>().Value);
        services.AddSingleton<IJobMonitorStore, JobMonitorStore>();
        services.AddSingleton<JobMonitorObserver>();
        services.AddHostedService<SampleEtlDemoHostedService>();
        return services;
    }
}
