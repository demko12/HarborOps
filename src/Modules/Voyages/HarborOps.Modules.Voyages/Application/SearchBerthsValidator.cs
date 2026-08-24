using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class SearchBerthsValidator
{
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";
    internal const string PortIdField = "portId";
    internal const string SearchField = "search";
    internal const string StatusField = "status";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;
    internal const int MaxSearchLength = Berth.NameMaxLength;

    internal static IReadOnlyDictionary<string, string[]> Validate(SearchBerthsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PortId is null || request.PortId == Guid.Empty)
        {
            errors[PortIdField] = ["Port is required."];
        }

        if (request.Search is { } search && search.Trim().Length > MaxSearchLength)
        {
            errors[SearchField] = [$"Search must not exceed {MaxSearchLength} characters."];
        }

        if (request.Status is { } status && status.Trim().Length > Berth.StatusMaxLength)
        {
            errors[StatusField] =
                [$"Status must not exceed {Berth.StatusMaxLength} characters."];
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
