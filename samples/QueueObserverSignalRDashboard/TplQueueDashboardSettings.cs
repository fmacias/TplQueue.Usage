using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Configuration;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal sealed class TplQueueDashboardSettings
{
    public const string SectionName = "TplQueue";

    public string MetadataDispatcherName { get; set; } = string.Empty;
    public string PayloadDispatcherName { get; set; } = string.Empty;
    public Dictionary<string, RetryPolicyDescriptor> RetryPolicies { get; set; } =
        new Dictionary<string, RetryPolicyDescriptor>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DispatcherDescriptor> Dispatchers { get; set; } =
        new Dictionary<string, DispatcherDescriptor>(StringComparer.OrdinalIgnoreCase);

    public static TplQueueDashboardSettings Load(IConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetRequiredSection(SectionName);
        var settings = section.Get<TplQueueDashboardSettings>();

        if (settings == null)
        {
            throw new InvalidOperationException(
                $"Configuration section '{SectionName}' is required for QueueObserverSignalRDashboard.");
        }

        settings.Validate();
        return settings;
    }

    public Dictionary<string, IRetryPolicyOptions> CreateRetryPolicies()
    {
        var retryPolicies = new Dictionary<string, IRetryPolicyOptions>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in RetryPolicies)
        {
            try
            {
                retryPolicies[entry.Key] = RetryPolicyOptions.Create(
                    entry.Value.BaseDelayMs,
                    entry.Value.MaxRetries,
                    entry.Value.Factor);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is ArgumentException)
            {
                throw new InvalidOperationException(
                    $"TplQueue retry policy '{entry.Key}' is invalid.",
                    ex);
            }
        }

        return retryPolicies;
    }

    public Dictionary<string, IQOptions> CreateDispatchers()
    {
        var dispatchers = new Dictionary<string, IQOptions>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Dispatchers)
        {
            try
            {
                var descriptor = entry.Value;
                var queueId = descriptor.Id == Guid.Empty ? Guid.NewGuid() : descriptor.Id;

                dispatchers[entry.Key] = new QOptions(
                    queueId,
                    descriptor.MaxParallelism,
                    descriptor.RetryPolicy);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is ArgumentException)
            {
                throw new InvalidOperationException(
                    $"TplQueue dispatcher '{entry.Key}' is invalid.",
                    ex);
            }
        }

        return dispatchers;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(MetadataDispatcherName))
        {
            throw new InvalidOperationException("TplQueue:MetadataDispatcherName is required.");
        }

        if (string.IsNullOrWhiteSpace(PayloadDispatcherName))
        {
            throw new InvalidOperationException("TplQueue:PayloadDispatcherName is required.");
        }

        if (RetryPolicies.Count == 0)
        {
            throw new InvalidOperationException("TplQueue:RetryPolicies must define at least one retry policy.");
        }

        if (Dispatchers.Count == 0)
        {
            throw new InvalidOperationException("TplQueue:Dispatchers must define at least one dispatcher.");
        }

        ValidateDispatcherReference(MetadataDispatcherName);
        ValidateDispatcherReference(PayloadDispatcherName);
    }

    private void ValidateDispatcherReference(string dispatcherName)
    {
        if (!Dispatchers.TryGetValue(dispatcherName, out var dispatcher))
        {
            throw new InvalidOperationException(
                $"TplQueue dispatcher '{dispatcherName}' was not found under TplQueue:Dispatchers.");
        }

        if (string.IsNullOrWhiteSpace(dispatcher.RetryPolicy))
        {
            throw new InvalidOperationException(
                $"TplQueue dispatcher '{dispatcherName}' must define a retry policy name.");
        }

        if (!RetryPolicies.ContainsKey(dispatcher.RetryPolicy))
        {
            throw new InvalidOperationException(
                $"TplQueue dispatcher '{dispatcherName}' references missing retry policy '{dispatcher.RetryPolicy}'.");
        }
    }

    internal sealed class RetryPolicyDescriptor
    {
        public int BaseDelayMs { get; set; }
        public int MaxRetries { get; set; }
        public double Factor { get; set; }
    }

    internal sealed class DispatcherDescriptor
    {
        public Guid Id { get; set; }
        public int MaxParallelism { get; set; }
        public string RetryPolicy { get; set; } = string.Empty;
    }
}
