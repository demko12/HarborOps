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
        return status switch
        {
            InProgressStatus => "in-progress",
            _ => status.ToLowerInvariant(),
        };
    }

    private static string ShortId(Guid id)
    {
        return id.ToString("N", CultureInfo.InvariantCulture)[..8];
    }

    internal static bool IsLowStock(decimal quantity, decimal minimumQuantity)
    {
        return quantity <= minimumQuantity;
    }

    internal static bool IsInsufficientStock(decimal quantity, decimal? quantityRequested)
    {
        return quantityRequested is { } requested && requested > quantity;
    }

    internal static readonly IReadOnlyList<string> Frequencies =
        ["Daily", "Weekly", "Monthly", "Quarterly", "SemiAnnual", "Annual", "RunningHours"];

    internal static string FieldLabel(string field)
    {
        return field switch
        {
            "vesselId" => "Vessel",
            "name" => "Name",
            "category" => "Category",
            "manufacturer" => "Manufacturer",
            "serialNumber" => "Serial number",
            "maintenanceType" => "Maintenance type",
            "frequency" => "Frequency",
            "nextDueDate" => "Next due date",
            "nextDueHours" => "Next due hours",
            "equipmentIds" => "Equipment",
            "completedOn" => "Completed on",
            "dueTo" => "Due to",
            "pageNumber" => "Page number",
            "pageSize" => "Page size",
            _ => field,
        };
    }

    internal static string OptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? MissingValue : value;
    }

    internal static string DueDate(DateOnly? value)
    {
        return value is { } dueDate
            ? dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string DueHours(int? value)
    {
        return value is { } hours
            ? hours.ToString(CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string DueState(bool isOverdue, int? daysUntilDue)
    {
        if (isOverdue)
        {
            return "Overdue";
        }

        return daysUntilDue switch
        {
            null => "Running hours only",
            0 => "Due today",
            1 => "Due in 1 day",
            _ => $"Due in {daysUntilDue.Value.ToString(CultureInfo.InvariantCulture)} days",
        };
    }

    internal static string DueStateModifier(bool isOverdue, int? daysUntilDue)
    {
        if (isOverdue)
        {
            return "overdue";
        }

        return daysUntilDue is null ? "hours" : "scheduled";
    }
}
