using Fmacias.TplQueue.Contracts;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal abstract class DashboardQueueRuntime
{
    protected DashboardQueueRuntime(
        string dispatcherName,
        IParallelQ queue,
        Func<IRetryPolicy> retryPolicyFactory)
    {
        DispatcherName = string.IsNullOrWhiteSpace(dispatcherName)
            ? throw new ArgumentException("A dispatcher name is required.", nameof(dispatcherName))
            : dispatcherName;
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _retryPolicyFactory = retryPolicyFactory ?? throw new ArgumentNullException(nameof(retryPolicyFactory));
    }

    private readonly Func<IRetryPolicy> _retryPolicyFactory;

    public string DispatcherName { get; }
    public IParallelQ Queue { get; }

    public Func<IRetryPolicy> CreateRetryPolicyFactory()
    {
        return _retryPolicyFactory;
    }
}

internal sealed class MetadataDashboardQueueRuntime : DashboardQueueRuntime
{
    public MetadataDashboardQueueRuntime(
        string dispatcherName,
        IParallelQ queue,
        Func<IRetryPolicy> retryPolicyFactory)
        : base(dispatcherName, queue, retryPolicyFactory)
    {
    }
}

internal sealed class PayloadDashboardQueueRuntime : DashboardQueueRuntime
{
    public PayloadDashboardQueueRuntime(
        string dispatcherName,
        IParallelQ queue,
        Func<IRetryPolicy> retryPolicyFactory)
        : base(dispatcherName, queue, retryPolicyFactory)
    {
    }
}
