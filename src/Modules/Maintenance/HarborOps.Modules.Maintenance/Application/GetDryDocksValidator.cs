using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetDryDocksValidator
{
    internal const string StatusField = "status";
    internal const string StartToField = "startTo";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetDryDocksRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(request.Status) && !TryParseStatus(request.Status, out _))
        {
            errors[StatusField] = [$"Must be one of {string.Join(", ", Enum.GetNames<DryDockStatus>())}."];
        }

        if (request.StartFrom is { } startFrom && request.StartTo is { } startTo && startTo < startFrom)
        {
            errors[StartToField] = ["Must be on or after the start-from date."];
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

    internal static bool TryParseStatus(string? value, out DryDockStatus status)
    {
        return Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
    }
}
