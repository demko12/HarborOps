using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class RevokeComplianceCertificateValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(RevokeComplianceCertificateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["A revocation reason is required."];
        }
        else if (request.Reason.Trim().Length < ComplianceCertificate.RevocationReasonMinLength)
        {
            errors[ReasonField] = [$"Reason must be at least {ComplianceCertificate.RevocationReasonMinLength} characters."];
        }
        else if (request.Reason.Trim().Length > ComplianceCertificate.RevocationReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {ComplianceCertificate.RevocationReasonMaxLength} characters."];
        }

        return errors;
    }
}
