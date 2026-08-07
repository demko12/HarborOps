using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.Modules.Fleet.Application;

internal static class SearchVesselsValidator
{
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string FleetIdField = "fleetId";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(SearchVesselsRequest request)
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

        if (request.FleetId == Guid.Empty)
        {
            errors[FleetIdField] = ["Fleet is required when supplied."];
        }

        return errors;
    }
}
