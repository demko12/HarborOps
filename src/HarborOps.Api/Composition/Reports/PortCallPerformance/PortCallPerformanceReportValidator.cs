using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public static class PortCallPerformanceReportValidator
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 10_000;

    public const string DepartedFromField = "departedFrom";
    public const string DepartedToField = "departedTo";
    public const string StatementAvailabilityField = "statementAvailability";
    public const string SortByField = "sortBy";
    public const string SortDirectionField = "sortDirection";
    public const string PageNumberField = "pageNumber";
    public const string PageSizeField = "pageSize";
    public const string LimitField = "limit";

    public static IReadOnlyDictionary<string, string[]> Validate(
        PortCallPerformanceReportFilter filter,
        int pageNumber,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (filter.DepartedFrom is null)
        {
            errors[DepartedFromField] = ["The departed-from date is required."];
        }

        if (filter.DepartedTo is null)
        {
            errors[DepartedToField] = ["The departed-to date is required."];
        }

        if (filter.DepartedFrom is { } from && filter.DepartedTo is { } to)
        {
            if (to < from)
            {
                errors[DepartedToField] = ["The departed-to date must not be earlier than the departed-from date."];
            }
            else if (to.DayNumber - from.DayNumber + 1 > ReportsPortCallPerformanceOptions.MaxRangeDays)
            {
                errors[DepartedToField] =
                    [$"The departure date range must not exceed {ReportsPortCallPerformanceOptions.MaxRangeDays} days."];
            }
        }

        if (Resolve(filter.StatementAvailability, ReportsPortCallPerformanceOptions.StatementAvailabilities) is null)
        {
            errors[StatementAvailabilityField] =
                [$"Statement availability must be one of: {string.Join(", ", ReportsPortCallPerformanceOptions.StatementAvailabilities)}."];
        }

        if (Resolve(filter.SortBy, ReportsPortCallPerformanceOptions.SortFields) is null)
        {
            errors[SortByField] =
                [$"Sort must be one of: {string.Join(", ", ReportsPortCallPerformanceOptions.SortFields)}."];
        }

        if (Resolve(filter.SortDirection, ReportsPortCallPerformanceOptions.SortDirections) is null)
        {
            errors[SortDirectionField] =
                [$"Sort direction must be one of: {string.Join(", ", ReportsPortCallPerformanceOptions.SortDirections)}."];
        }

        if (pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be greater than zero."];
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        return errors;
    }

    public static string? Resolve(string? value, IReadOnlyList<string> allowed) =>
        string.IsNullOrWhiteSpace(value)
            ? allowed[0]
            : allowed.FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
}
