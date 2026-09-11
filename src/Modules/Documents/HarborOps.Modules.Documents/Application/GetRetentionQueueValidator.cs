using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class GetRetentionQueueValidator
{
    internal const string StateField = "state";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    private static readonly string[] KnownStates =
    [
        DocumentRetentionState.ActiveState,
        DocumentRetentionState.ArchivedState,
        DocumentRetentionState.PendingDispositionState,
        DocumentRetentionState.DisposedState,
    ];

    internal static IReadOnlyDictionary<string, string[]> Validate(GetRetentionQueueRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.State is { } state && !KnownStates.Contains(state, StringComparer.Ordinal))
        {
            errors[StateField] = [$"State must be one of: {string.Join(", ", KnownStates)}."];
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
