namespace HarborOps.Modules.Compliance.Application;

internal sealed class ComplianceCertificateNotFoundException(Guid certificateId)
    : Exception($"Compliance certificate '{certificateId}' was not found.")
{
    internal Guid CertificateId { get; } = certificateId;
}

internal sealed class ComplianceCertificateConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class RenewComplianceCertificateValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Renew compliance certificate validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RevokeComplianceCertificateValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Revoke compliance certificate validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
