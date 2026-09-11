using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class PortCallClearanceValidator
{
    internal const string CategoryField = "category";
    internal const string TitleField = "title";
    internal const string DueAtUtcField = "dueAtUtc";
    internal const string AuthorityReferenceField = "authorityReference";
    internal const string EvidenceDocumentIdField = "evidenceDocumentId";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> ValidateAdd(
        AddPortCallClearanceItemRequest request)
    {
        return ValidateItemDefinition(
            request.Category,
            request.Title,
            request.DueAtUtc,
            request.AuthorityReference,
            request.EvidenceDocumentId);
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateUpdate(
        UpdatePortCallClearanceItemRequest request)
    {
        return ValidateItemDefinition(
            request.Category,
            request.Title,
            request.DueAtUtc,
            request.AuthorityReference,
            request.EvidenceDocumentId);
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateClear(
        ClearPortCallClearanceItemRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateAuthorityReference(request.AuthorityReference, errors);
        ValidateEvidenceDocumentId(request.EvidenceDocumentId, errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateWaive(
        WaivePortCallClearanceItemRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateReason(request.Reason, "Waiver reason", errors);

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateReopen(
        ReopenPortCallClearanceRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateReason(request.Reason, "Reopen reason", errors);

        return errors;
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string[]> ValidateItemDefinition(
        string? category,
        string? title,
        DateTime? dueAtUtc,
        string? authorityReference,
        Guid? evidenceDocumentId)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(category))
        {
            errors[CategoryField] = ["Category is required."];
        }
        else if (!PortCallClearanceCategory.IsValid(category.Trim()))
        {
            errors[CategoryField] =
                [$"Category must be one of: {string.Join(", ", PortCallClearanceCategory.All)}."];
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            errors[TitleField] = ["Title is required."];
        }
        else if (title.Trim().Length > PortCallClearanceItem.TitleMaxLength)
        {
            errors[TitleField] =
                [$"Title must not exceed {PortCallClearanceItem.TitleMaxLength} characters."];
        }

        if (dueAtUtc is { } dueAt && dueAt.Kind != DateTimeKind.Utc)
        {
            errors[DueAtUtcField] = ["Due time must be a UTC timestamp."];
        }

        ValidateAuthorityReference(authorityReference, errors);
        ValidateEvidenceDocumentId(evidenceDocumentId, errors);

        return errors;
    }

    private static void ValidateAuthorityReference(
        string? authorityReference,
        IDictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(authorityReference)
            && authorityReference.Trim().Length > PortCallClearanceItem.AuthorityReferenceMaxLength)
        {
            errors[AuthorityReferenceField] =
            [
                $"Authority reference must not exceed {PortCallClearanceItem.AuthorityReferenceMaxLength} characters.",
            ];
        }
    }

    private static void ValidateEvidenceDocumentId(
        Guid? evidenceDocumentId,
        IDictionary<string, string[]> errors)
    {
        if (evidenceDocumentId == Guid.Empty)
        {
            errors[EvidenceDocumentIdField] =
                ["Evidence document must be a valid identifier when supplied."];
        }
    }

    private static void ValidateReason(
        string? reason,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[ReasonField] = [$"{displayName} is required."];
        }
        else if (reason.Trim().Length > PortCallClearanceEvent.ReasonMaxLength)
        {
            errors[ReasonField] =
                [$"{displayName} must not exceed {PortCallClearanceEvent.ReasonMaxLength} characters."];
        }
    }
}
