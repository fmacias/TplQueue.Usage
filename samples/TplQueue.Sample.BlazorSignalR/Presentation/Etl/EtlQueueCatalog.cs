using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Etl;

/// <summary>Describes one backend-owned queue shown by the dashboard.</summary>
internal sealed record EtlQueueDescriptor(
    AvailableQueue Queue,
    Guid QueueId,
    string GroupId,
    string DisplayName,
    int Order);

/// <summary>Maps runtime queue identifiers to stable presentation groups.</summary>
internal sealed class EtlQueueCatalog
{
    private readonly IReadOnlyList<EtlQueueDescriptor> _descriptors;
    private readonly IReadOnlyDictionary<Guid, EtlQueueDescriptor> _byQueueId;

    public EtlQueueCatalog(IEnumerable<EtlQueueDescriptor> descriptors)
    {
        if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));

        var ordered = descriptors.OrderBy(descriptor => descriptor.Order).ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one ETL queue descriptor is required.", nameof(descriptors));
        }

        if (ordered.Any(descriptor =>
                descriptor.QueueId == Guid.Empty ||
                string.IsNullOrWhiteSpace(descriptor.GroupId) ||
                string.IsNullOrWhiteSpace(descriptor.DisplayName)))
        {
            throw new ArgumentException("ETL queue descriptors must contain stable identifiers and names.", nameof(descriptors));
        }

        if (ordered.Select(descriptor => descriptor.QueueId).Distinct().Count() != ordered.Length ||
            ordered.Select(descriptor => descriptor.GroupId).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
        {
            throw new ArgumentException("ETL queue identifiers and group names must be unique.", nameof(descriptors));
        }

        _descriptors = Array.AsReadOnly(ordered);
        _byQueueId = ordered.ToDictionary(descriptor => descriptor.QueueId);
    }

    public IReadOnlyList<EtlQueueDescriptor> Descriptors => _descriptors;

    public EtlQueueDescriptor Resolve(Guid queueId)
    {
        if (_byQueueId.TryGetValue(queueId, out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException(
            $"Queue '{queueId}' is not part of the ETL dashboard catalog.");
    }
}
