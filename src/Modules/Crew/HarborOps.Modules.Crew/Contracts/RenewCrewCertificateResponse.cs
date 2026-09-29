namespace HarborOps.Modules.Crew.Contracts;

public sealed record RenewCrewCertificateResponse(
    Guid Id,
    Guid PredecessorCertificateId,
    Guid CrewMemberId,
    string Type,
    string Status,
    Guid ConcurrencyToken);
