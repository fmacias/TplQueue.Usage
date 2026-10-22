using System.Text.Json;

namespace TplQueue.Sample.BlazorSignalR.Presentation.Monitoring;

/// <summary>Encodes original snapshot metadata without flattening dependency graphs for JS interop.</summary>
internal static class JobMonitorJson
{
    // Nested job objects and dependency arrays need more depth than the default interop serializer.
    // This accommodates the product's 512-level job graphs, with room for the transport envelope.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 2048 };

    /// <summary>Uses a JSON string at the interop boundary so its serializer does not traverse the graph again.</summary>
    public static string Serialize(JobMonitorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }
}
