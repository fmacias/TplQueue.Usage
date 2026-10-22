namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>Bounds retained observation count. This is not a byte limit on individual snapshot graphs.</summary>
internal sealed class JobMonitorRetentionOptions
{
    /// <summary>
    /// Oldest observed jobs are evicted as whole units when this limit is exceeded, even if active.
    /// A single job exceeding the limit is also evicted. Later observations can introduce it again.
    /// </summary>
    public int MaximumEvents { get; set; } = 20_000;
}
