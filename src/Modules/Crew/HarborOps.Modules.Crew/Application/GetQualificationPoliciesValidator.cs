using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class GetQualificationPoliciesValidator
{
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetQualificationPoliciesRequest request)
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

        return errors;
    }
}
