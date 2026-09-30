namespace HarborOps.Modules.Crew.Contracts;

public sealed record RevokeCrewCertificateResponse(
    Guid Id,
    DateTime RevokedAtUtc,
    Guid ConcurrencyToken,
    IReadOnlyList<AffectedCertificateAssignment> AffectedActiveAssignments);
