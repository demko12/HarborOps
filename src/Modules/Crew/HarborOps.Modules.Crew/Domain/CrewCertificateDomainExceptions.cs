namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewCertificateAlreadyRevokedException(Guid certificateId)
    : Exception($"Crew certificate '{certificateId}' is already revoked.")
{
    internal Guid CertificateId { get; } = certificateId;
}
