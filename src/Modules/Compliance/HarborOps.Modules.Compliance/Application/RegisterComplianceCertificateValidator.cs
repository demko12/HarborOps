using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class RegisterComplianceCertificateValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string CertificateTypeField = "certificateType";
    internal const string CertificateNumberField = "certificateNumber";
    internal const string IssuingAuthorityField = "issuingAuthority";
    internal const string IssuedOnField = "issuedOn";
    internal const string ExpiresOnField = "expiresOn";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterComplianceCertificateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredGuid(
            request.VesselId,
            VesselIdField,
            "Vessel",
            errors);

        ValidateRequiredLength(
            request.CertificateType,
            ComplianceCertificate.CertificateTypeMaxLength,
            CertificateTypeField,
            "Certificate type",
            errors);

        ValidateRequiredLength(
            request.CertificateNumber,
            ComplianceCertificate.CertificateNumberMaxLength,
            CertificateNumberField,
            "Certificate number",
            errors);

        ValidateRequiredLength(
            request.IssuingAuthority,
            ComplianceCertificate.IssuingAuthorityMaxLength,
            IssuingAuthorityField,
            "Issuing authority",
            errors);

        ValidateDateRange(request.IssuedOn, request.ExpiresOn, errors);

        return errors;
    }

    private static void ValidateDateRange(
        DateOnly? issuedOn,
        DateOnly? expiresOn,
        IDictionary<string, string[]> errors)
    {
        if (issuedOn is null)
        {
            errors[IssuedOnField] = ["Issued on date is required."];
        }

        if (expiresOn is null)
        {
            errors[ExpiresOnField] = ["Expires on date is required."];
            return;
        }

        if (issuedOn is not null && expiresOn.Value < issuedOn.Value)
        {
            errors[ExpiresOnField] = ["Expires on date must not be earlier than issued on date."];
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
