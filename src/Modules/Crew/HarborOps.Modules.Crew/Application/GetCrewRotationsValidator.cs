using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class GetCrewRotationsValidator
{
    internal const string FromField = "from";
    internal const string ToField = "to";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetCrewRotationsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.From is not null && request.To is not null && request.To.Value < request.From.Value)
        {
            errors[ToField] = ["To date cannot precede the from date."];
        }

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

        return errors;
    }
}
