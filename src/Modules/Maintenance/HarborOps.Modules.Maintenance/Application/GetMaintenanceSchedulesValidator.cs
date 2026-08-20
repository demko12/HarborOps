using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetMaintenanceSchedulesValidator
{
    internal const string DueToField = "dueTo";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetMaintenanceSchedulesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.DueFrom is { } dueFrom && request.DueTo is { } dueTo && dueTo < dueFrom)
        {
            errors[DueToField] = ["Must be on or after the due-from date."];
        }

        if (request.PageNumber.HasValue && request.PageNumber.Value < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize.HasValue && request.PageSize.Value < 1)
        {
            errors[PageSizeField] = ["Page size must be 1 or greater."];
        }
        else if (request.PageSize.HasValue && request.PageSize.Value > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
        }

        return errors;
    }
}
