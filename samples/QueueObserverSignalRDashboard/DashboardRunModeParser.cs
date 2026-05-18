namespace TplQueue.Usage.QueueObserverSignalRDashboard;

internal static class DashboardRunModeParser
{
    public static bool TryParse(string? value, out DashboardRunMode mode)
    {
        if (string.Equals(value, "wait", StringComparison.OrdinalIgnoreCase))
        {
            mode = DashboardRunMode.Wait;
            return true;
        }

        if (string.Equals(value, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            mode = DashboardRunMode.Cancel;
            return true;
        }

        mode = default;
        return false;
    }
}
