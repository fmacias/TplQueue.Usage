using Fmacias.TplQueue.Contracts;

namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal abstract class DashboardQueueRuntime
{
    protected DashboardQueueRuntime(
        string queueName,
        IParallelQ queue)
    {
        QueueName = string.IsNullOrWhiteSpace(queueName)
            ? throw new ArgumentException("A dispatcher name is required.", nameof(queueName))
            : queueName;
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public string QueueName { get; }
    public IParallelQ Queue { get; }

    /// <summary>
    /// Get retry policy used at Queue level
    /// </summary>
    /// <returns></returns>
    public Func<IRetryPolicy> GetQueueRetryPolicyDelegate()
    {
        return Queue.RetryPolicyFactory;
    }
}

internal sealed class MetadataDashboardQueueRuntime : DashboardQueueRuntime
{
    public MetadataDashboardQueueRuntime(
        string queueName,
        IParallelQ queue)
        : base(queueName, queue)
    {
    }
}

internal sealed class PayloadDashboardQueueRuntime : DashboardQueueRuntime
{
    public PayloadDashboardQueueRuntime(
        string queueName,
        IParallelQ queue)
        : base(queueName, queue)
    {
    }
}
