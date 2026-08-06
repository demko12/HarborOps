using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class RegisterDocumentValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string DocumentTypeIdField = "documentTypeId";
    internal const string NumberField = "number";
    internal const string IssueDateField = "issueDate";
    internal const string ExpiryDateField = "expiryDate";
    internal const string IssuingAuthorityField = "issuingAuthority";
    internal const string StatusField = "status";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterDocumentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredGuid(
            request.VesselId,
            VesselIdField,
            "Vessel",
            errors);

        ValidateRequiredDocumentTypeId(request.DocumentTypeId, errors);

        ValidateRequiredLength(
            request.Number,
            Document.NumberMaxLength,
            NumberField,
            "Document number",
            errors);

        ValidateDates(request.IssueDate, request.ExpiryDate, errors);

        ValidateRequiredLength(
            request.IssuingAuthority,
            Document.IssuingAuthorityMaxLength,
            IssuingAuthorityField,
            "Issuing authority",
            errors);

        ValidateRequiredLength(
            request.Status,
            Document.StatusMaxLength,
            StatusField,
            "Status",
            errors);

        return errors;
    }

    private static void ValidateRequiredDocumentTypeId(
        int? value,
        IDictionary<string, string[]> errors)
    {
        if (value is null || value <= 0)
        {
            errors[DocumentTypeIdField] = ["Document type is required."];
        }
    }

    private static void ValidateDates(
        DateOnly? issueDate,
        DateOnly? expiryDate,
        IDictionary<string, string[]> errors)
    {
        if (issueDate is null)
        {
            errors[IssueDateField] = ["Issue date is required."];
        }

        if (expiryDate is null)
        {
            errors[ExpiryDateField] = ["Expiry date is required."];
        }
        else if (issueDate is not null && expiryDate < issueDate)
        {
            errors[ExpiryDateField] = ["Expiry date must not be earlier than the issue date."];
        }
    }

    private static void ValidateRequiredGuid(
        Guid? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null || value == Guid.Empty)
        {
            errors[field] = [$"{displayName} is required."];
        }
    }

    private static void ValidateRequiredLength(
        string? value,
        int maxLength,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{displayName} must not exceed {maxLength} characters."];
        }
    }
}
