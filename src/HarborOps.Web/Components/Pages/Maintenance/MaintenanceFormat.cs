using System.Globalization;

namespace HarborOps.Web.Components.Pages.Maintenance;

internal static class MaintenanceFormat
{
    internal const string CriticalPriority = "Critical";
    internal const string OpenStatus = "Open";
    internal const string InProgressStatus = "InProgress";
    internal const string CompletedStatus = "Completed";
    internal const string CanceledStatus = "Canceled";
    internal const string MissingValue = "—";

    internal static readonly IReadOnlyList<string> Statuses =
        [OpenStatus, InProgressStatus, CompletedStatus, CanceledStatus];

    internal static readonly IReadOnlyList<string> Priorities =
        ["Low", "Medium", "High", CriticalPriority];

    private static readonly string[] CompletableStatuses = [OpenStatus, InProgressStatus];

    internal static bool IsCritical(string priority)
    {
        return string.Equals(priority, CriticalPriority, StringComparison.Ordinal);
    }

    internal static bool CanComplete(string status)
    {
        return CompletableStatuses.Contains(status, StringComparer.Ordinal);
    }

    internal static bool IsOverdue(string status, DateTime dueUtc, DateTime utcNow)
    {
        return CanComplete(status) && dueUtc < utcNow;
    }

    internal static string DueUtc(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string CreatedUtc(DateTime value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string CompletedUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string AssignedEngineer(Guid? assignedEngineerId)
    {
        return assignedEngineerId is { } id ? ShortId(id) : "Unassigned";
    }

    internal static string ShortVesselReference(Guid vesselId)
    {
        return $"Vessel {ShortId(vesselId)}";
    }

    internal static string PriorityModifier(string priority)
    {
        return priority.ToLowerInvariant();
    }

    internal static string StatusModifier(string status)
    {
        return status.ToLowerInvariant();
    }

    private static string ShortId(Guid id)
    {
        return id.ToString("N", CultureInfo.InvariantCulture)[..8];
    }
}
