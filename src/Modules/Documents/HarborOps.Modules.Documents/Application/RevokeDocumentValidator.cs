using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class RevokeDocumentValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(RevokeDocumentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Reason is required."];
        }
        // Revoke reason is persisted as DocumentReviewEvent.Notes (same column, same
        // action-agnostic free-text field as a rejection's notes), so it is bound by
        // the same length limit rather than a separate Reason-specific one.
        else if (request.Reason.Trim().Length > DocumentReviewEvent.NotesMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {DocumentReviewEvent.NotesMaxLength} characters."];
        }

        return errors;
    }
}
