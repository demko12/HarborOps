namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewCertificateHistoryItem(
    Guid Id,
    Guid CertificateId,
    string EventType,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    string? Reason,
    Guid? PredecessorCertificateId);
