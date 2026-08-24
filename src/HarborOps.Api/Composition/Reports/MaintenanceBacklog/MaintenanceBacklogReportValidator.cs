namespace HarborOps.Api.Composition.Reports.MaintenanceBacklog;

public static class MaintenanceBacklogReportValidator
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 5000;

    public const string AsOfField = "asOf";
    public const string PageNumberField = "pageNumber";
    public const string PageSizeField = "pageSize";
    public const string PriorityField = "priority";
    public const string StatusField = "status";
    public const string DateRangeField = "dueTo";

    public static readonly string[] ValidPriorities = ["Low", "Medium", "High", "Critical"];
    public static readonly string[] ValidStatuses = ["Open", "InProgress", "Completed", "Canceled"];

    public static IReadOnlyDictionary<string, string[]> Validate(
        DateOnly? asOf,
        int pageNumber,
        int pageSize,
        string? priority,
        string? status,
        DateOnly? dueFrom,
        DateOnly? dueTo)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (asOf is null)
        {
            errors[AsOfField] = ["The asOf date is required."];
        }

        if (pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be greater than zero."];
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        if (!string.IsNullOrWhiteSpace(priority) && !ValidPriorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
        {
            errors[PriorityField] = [$"Priority must be one of: {string.Join(", ", ValidPriorities)}."];
        }

        if (!string.IsNullOrWhiteSpace(status) && !ValidStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            errors[StatusField] = [$"Status must be one of: {string.Join(", ", ValidStatuses)}."];
        }

        if (dueFrom is { } from && dueTo is { } to && to < from)
        {
            errors[DateRangeField] = ["Due-to date must not be earlier than the due-from date."];
        }

        return errors;
    }
}
