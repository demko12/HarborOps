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

    internal static string UnknownVessel() => "Unknown vessel";

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
            "portId" => "Port",
            "startDate" => "Start date",
            "startTo" => "Start to",
            "plannedTasks" => "Planned tasks",
            "endDate" => "End date",
            "actualCost" => "Actual cost",
            "status" => "Status",
            "technicianId" => "Technician",
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

    internal static readonly IReadOnlyList<string> DryDockStatuses =
        ["Planned", "InProgress", "Completed", "Canceled"];

    internal static string DryDockStatusLabel(string status)
    {
        return string.Equals(status, "InProgress", StringComparison.Ordinal) ? "In progress" : status;
    }

    internal const int DryDockLifecycleStepCount = 3;

    internal static int DryDockLifecycleStep(string status)
    {
        return status switch
        {
            "InProgress" => 2,
            "Completed" => DryDockLifecycleStepCount,
            "Canceled" => 0,
            _ => 1,
        };
    }

    internal static string DryDockStatusModifier(string status)
    {
        return status switch
        {
            "Completed" => "done",
            "Canceled" => "cancelled",
            "InProgress" => "active",
            _ => "pending",
        };
    }

    internal static string StartDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string EndDate(DateOnly? value)
    {
        return value is { } endDate
            ? endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string ActualCost(decimal? value)
    {
        return value is { } cost
            ? cost.ToString("N2", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string DryDockTiming(string status, bool isOverdue, int? daysUntilStart)
    {
        if (isOverdue)
        {
            return "Overdue";
        }

        if (daysUntilStart is null)
        {
            return DryDockStatusLabel(status);
        }

        return daysUntilStart.Value switch
        {
            0 => "Starts today",
            1 => "Starts in 1 day",
            _ => $"Starts in {daysUntilStart.Value.ToString(CultureInfo.InvariantCulture)} days",
        };
    }

    internal static string ShortPortReference(Guid portId)
    {
        return $"Port {ShortId(portId)}";
    }

    internal static string ActionLabel(string action)
    {
        return action switch
        {
            "Start" => "Start work order",
            "Cancel" => "Cancel work order",
            "ReplaceEquipment" => "Change equipment",
            _ => action,
        };
    }

    internal static string CompletionDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string Money(decimal? value)
    {
        return value is { } amount
            ? amount.ToString("N2", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string LaborHours(decimal? value)
    {
        return value is { } hours
            ? hours.ToString("N2", CultureInfo.InvariantCulture)
            : MissingValue;
    }
}
