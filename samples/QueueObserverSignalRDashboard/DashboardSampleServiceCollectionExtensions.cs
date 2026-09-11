using Fmacias.TplQueue.Contracts;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal static class DashboardSampleServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardSample(
        this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<DashboardRunStore>();
        services.AddSingleton<DashboardNotifier>();
        services.AddSingleton<MetadataDashboardQueueRuntime>(serviceProvider =>
            CreateQueueRuntime<MetadataDashboardQueueRuntime>(
                serviceProvider,
                "dashboard-metadata",
                (dispatcherName, queue) =>
                    new MetadataDashboardQueueRuntime(dispatcherName, queue)));
        services.AddSingleton<PayloadDashboardQueueRuntime>(serviceProvider =>
            CreateQueueRuntime<PayloadDashboardQueueRuntime>(
                serviceProvider,
                "dashboard-payload",
                (dispatcherName, queue) =>
                    new PayloadDashboardQueueRuntime(dispatcherName, queue)));
        services.AddSingleton<PayloadDashboardWorkflowFactory>();
        services.AddSingleton<DashboardRunCoordinator>();

        return services;
    }

    private static TQueueRuntime CreateQueueRuntime<TQueueRuntime>(
        IServiceProvider serviceProvider,
        string queueName,
        Func<string, IParallelQ, TQueueRuntime> factory)
        where TQueueRuntime : DashboardQueueRuntime
    {
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        if (string.IsNullOrWhiteSpace(queueName)) throw new ArgumentException("A dispatcher name is required.", nameof(queueName));
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var queueFactory = serviceProvider.GetRequiredService<IQFactoryAdapter>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var configuredQueues = serviceProvider.GetRequiredService<IApi>().QueueOptions;

        if (!configuredQueues.TryGetValue(queueName, out var queueOptions))
        {
            throw new InvalidOperationException(
                $"Dispatcher '{queueName}' was not registered for the dashboard sample.");
        }
        return factory(queueName, queueFactory.Parallel(queueName, loggerFactory.CreateLogger<IParallelQ>()));
    }
}
