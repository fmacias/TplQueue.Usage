namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal static class SamplePaths
{
    public static string ResolveSampleRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "QueueObserverSignalRDashboard.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
