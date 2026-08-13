using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class SearchCargoBookingsValidator
{
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string StatusField = "status";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(
        SearchCargoBookingsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            if (pageSize < 1)
            {
                errors[PageSizeField] = ["Page size must be 1 or greater."];
            }
            else if (pageSize > MaxPageSize)
            {
                errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && !TryParseStatus(request.Status, out _))
        {
            errors[StatusField] = ["Status is not a known cargo status."];
        }

        return errors;
    }

    internal static bool TryParseStatus(string? status, out CargoStatus parsedStatus)
    {
        return Enum.TryParse(status, ignoreCase: true, out parsedStatus)
            && Enum.IsDefined(parsedStatus);
    }
}
