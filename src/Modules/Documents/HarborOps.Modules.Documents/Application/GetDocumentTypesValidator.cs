using HarborOps.Modules.Documents.Contracts;

namespace HarborOps.Modules.Documents.Application;

internal static class GetDocumentTypesValidator
{
    internal const string SearchField = "search";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageSize = 100;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetDocumentTypesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

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
