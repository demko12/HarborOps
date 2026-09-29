namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewCertificateNotFoundException(Guid certificateId)
    : Exception($"Crew certificate with ID '{certificateId}' was not found.")
{
    internal Guid CertificateId { get; } = certificateId;
}

internal sealed class DuplicateCrewCertificateRenewalException(Guid predecessorCertificateId)
    : Exception($"Crew certificate '{predecessorCertificateId}' already has a renewal successor.")
{
    internal Guid PredecessorCertificateId { get; } = predecessorCertificateId;
}

internal sealed class RenewCrewCertificateValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Renew crew certificate validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RevokeCrewCertificateValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Revoke crew certificate validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
