using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class SearchPortsValidator
{
    internal const string CountryField = "country";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string SearchField = "search";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;
    internal const int MaxSearchLength = Port.NameMaxLength;

    internal static IReadOnlyDictionary<string, string[]> Validate(SearchPortsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.Search is { } search && search.Trim().Length > MaxSearchLength)
        {
            errors[SearchField] = [$"Search must not exceed {MaxSearchLength} characters."];
        }

        if (request.Country is { } country && country.Trim().Length > Port.CountryMaxLength)
        {
            errors[CountryField] =
                [$"Country must not exceed {Port.CountryMaxLength} characters."];
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
