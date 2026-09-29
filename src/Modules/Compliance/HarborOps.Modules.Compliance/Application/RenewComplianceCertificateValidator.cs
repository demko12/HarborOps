using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class RenewComplianceCertificateValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string CertificateNumberField = "certificateNumber";
    internal const string IssuingAuthorityField = "issuingAuthority";
    internal const string IssuedOnField = "issuedOn";
    internal const string ExpiresOnField = "expiresOn";

    internal static IReadOnlyDictionary<string, string[]> Validate(RenewComplianceCertificateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

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

        if (request.IssuedOn is null)
        {
            errors[IssuedOnField] = ["Issued on date is required."];
        }

        if (request.ExpiresOn is null)
        {
            errors[ExpiresOnField] = ["Expires on date is required."];
        }
        else if (request.IssuedOn is not null && request.ExpiresOn.Value < request.IssuedOn.Value)
        {
            errors[ExpiresOnField] = ["Expires on date must not be earlier than issued on date."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstPredecessor(
        RenewComplianceCertificateRequest request,
        ComplianceCertificate predecessor)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.IssuedOn!.Value < predecessor.IssuedOn)
        {
            errors[IssuedOnField] = ["Issued on date must not be earlier than the renewed certificate's issued on date."];
        }

        if (request.ExpiresOn!.Value <= predecessor.ExpiresOn)
        {
            errors[ExpiresOnField] = ["Expires on date must be later than the renewed certificate's expiry date."];
        }

        return errors;
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
