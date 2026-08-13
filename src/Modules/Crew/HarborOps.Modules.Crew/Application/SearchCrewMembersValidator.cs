using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class SearchCrewMembersValidator
{
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(SearchCrewMembersRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            errors[PageSizeField] = pageSize switch
            {
                < 1 => ["Page size must be 1 or greater."],
                > MaxPageSize => [$"Page size must not exceed {MaxPageSize}."],
                _ => [],
            };
        }

        return errors.Where(error => error.Value.Length > 0).ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal);
    }
}
