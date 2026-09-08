using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class ReviewDocumentValidator
{
    internal const string DecisionField = "decision";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> Validate(ReviewDocumentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var isApproved = string.Equals(request.Decision, Document.ApprovedReviewStatus, StringComparison.Ordinal);
        var isRejected = string.Equals(request.Decision, Document.RejectedReviewStatus, StringComparison.Ordinal);

        if (!isApproved && !isRejected)
        {
            errors[DecisionField] = ["Decision must be either \"Approved\" or \"Rejected\"."];
        }

        if (isRejected)
        {
            if (string.IsNullOrWhiteSpace(request.Notes))
            {
                errors[NotesField] = ["Notes are required when rejecting a document."];
            }
            else if (request.Notes.Trim().Length > DocumentReviewEvent.NotesMaxLength)
            {
                errors[NotesField] = [$"Notes must not exceed {DocumentReviewEvent.NotesMaxLength} characters."];
            }
        }

        return errors;
    }
}
