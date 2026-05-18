using Fmacias.TplQueue;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Microsoft.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal static class DashboardSampleServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardSample(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        var settings = TplQueueDashboardSettings.Load(configuration);
        var retryPolicies = settings.CreateRetryPolicies();
        var dispatchers = settings.CreateDispatchers();
        var api = API.Create(CoreApi.Create(), retryPolicies, dispatchers);

        services.AddSingleton(settings);
        services.AddTplQueue(api, retryPolicies, dispatchers);
        services.AddSingleton<DashboardRunStore>();
        services.AddSingleton<DashboardNotifier>();
        services.AddSingleton<ISystemTextJsonUniversalSerializer>(serviceProvider =>
            serviceProvider
                .GetRequiredService<ISystemTextJsonSerializerFactory>()
                .Serializer(new JsonSerializerOptions { WriteIndented = true }));
        services.AddSingleton<IXmlUniversalSerializer>(serviceProvider =>
            serviceProvider
                .GetRequiredService<IXmlSerializerFactory>()
                .Serializer());
        services.AddSingleton<MetadataDashboardQueueRuntime>(serviceProvider =>
            CreateQueueRuntime<MetadataDashboardQueueRuntime>(
                serviceProvider,
                settings.MetadataDispatcherName,
                (dispatcherName, queue, retryPolicyFactory) =>
                    new MetadataDashboardQueueRuntime(dispatcherName, queue, retryPolicyFactory)));
        services.AddSingleton<PayloadDashboardQueueRuntime>(serviceProvider =>
            CreateQueueRuntime<PayloadDashboardQueueRuntime>(
                serviceProvider,
                settings.PayloadDispatcherName,
                (dispatcherName, queue, retryPolicyFactory) =>
                    new PayloadDashboardQueueRuntime(dispatcherName, queue, retryPolicyFactory)));
        services.AddSingleton<PayloadDashboardWorkflowFactory>();
        services.AddSingleton<DashboardRunCoordinator>();

        return services;
    }

    private static TQueueRuntime CreateQueueRuntime<TQueueRuntime>(
        IServiceProvider serviceProvider,
        string dispatcherName,
        Func<string, IParallelQ, Func<IRetryPolicy>, TQueueRuntime> factory)
        where TQueueRuntime : DashboardQueueRuntime
    {
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        if (string.IsNullOrWhiteSpace(dispatcherName)) throw new ArgumentException("A dispatcher name is required.", nameof(dispatcherName));
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var queueFactory = serviceProvider.GetRequiredService<IQFactoryAdapter>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var retryPolicyFactory = serviceProvider.GetRequiredService<IRetryPolicyAbstractFactory>();
        var retryPolicies = serviceProvider.GetRequiredService<IReadOnlyDictionary<string, IRetryPolicyOptions>>();
        var dispatchers = serviceProvider.GetRequiredService<IReadOnlyDictionary<string, IQOptions>>();

        if (!dispatchers.TryGetValue(dispatcherName, out var dispatcherOptions))
        {
            throw new InvalidOperationException(
                $"Dispatcher '{dispatcherName}' was not registered for the dashboard sample.");
        }

        var queue = queueFactory.Parallel(dispatcherName, loggerFactory.CreateLogger<IParallelQ>());
        Func<IRetryPolicy> createRetryPolicy = () =>
            retryPolicyFactory.PolicyByName(dispatcherOptions.RetryPolicy, retryPolicies);

        return factory(dispatcherName, queue, createRetryPolicy);
    }
}
