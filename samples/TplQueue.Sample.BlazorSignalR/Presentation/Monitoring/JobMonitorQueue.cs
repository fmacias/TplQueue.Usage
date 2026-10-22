namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>Connects the snapshot's runtime queue ID to a stable display ID and configured capacity.</summary>
public sealed record JobMonitorQueue(Guid QueueId, string Id, string Name, int MaxParallelism);
