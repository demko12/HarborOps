using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class RegisterCrewCertificateValidator
{
    internal const string CrewMemberIdField = "crewMemberId";
    internal const string TypeField = "type";
    internal const string NameField = "name";
    internal const string IssuingAuthorityField = "issuingAuthority";
    internal const string LicenseNumberField = "licenseNumber";
    internal const string IssueDateField = "issueDate";
    internal const string ExpiryDateField = "expiryDate";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterCrewCertificateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.Type,
            CrewCertificate.TypeMaxLength,
            TypeField,
            "Type",
            errors);

        ValidateRequiredLength(
            request.Name,
            CrewCertificate.NameMaxLength,
            NameField,
            "Name",
            errors);

        ValidateRequiredLength(
            request.IssuingAuthority,
            CrewCertificate.IssuingAuthorityMaxLength,
            IssuingAuthorityField,
            "Issuing authority",
            errors);

        ValidateRequiredLength(
            request.LicenseNumber,
            CrewCertificate.LicenseNumberMaxLength,
            LicenseNumberField,
            "License number",
            errors);

        if (request.IssueDate is null)
        {
            errors[IssueDateField] = ["Issue date is required."];
        }

        ValidateExpiryDate(request.IssueDate, request.ExpiryDate, errors);

        return errors;
    }

    private static void ValidateExpiryDate(
        DateOnly? issueDate,
        DateOnly? expiryDate,
        IDictionary<string, string[]> errors)
    {
        if (expiryDate is null)
        {
            errors[ExpiryDateField] = ["Expiry date is required."];
            return;
        }

        if (issueDate is not null && expiryDate.Value < issueDate.Value)
        {
            errors[ExpiryDateField] = ["Expiry date must not be earlier than the issue date."];
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
