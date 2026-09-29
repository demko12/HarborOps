namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewCertificateHistory
{
    internal const int EventTypeMaxLength = 20;
    internal const int ReasonMaxLength = 500;

    internal const string RenewedEvent = "Renewed";
    internal const string RevokedEvent = "Revoked";

    private CrewCertificateHistory()
    {
    }

    private CrewCertificateHistory(
        Guid id,
        Guid certificateId,
        string eventType,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string? reason,
        Guid? predecessorCertificateId)
    {
        Id = id;
        CertificateId = certificateId;
        EventType = eventType;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        Reason = reason;
        PredecessorCertificateId = predecessorCertificateId;
    }

    public Guid Id { get; private set; }
    public Guid CertificateId { get; private set; }
    public string EventType { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public Guid? PredecessorCertificateId { get; private set; }

    internal static CrewCertificateHistory RecordRenewed(
        Guid certificateId, Guid actorUserId, DateTime occurredAtUtc, Guid predecessorCertificateId)
    {
        return new CrewCertificateHistory(
            Guid.NewGuid(), certificateId, RenewedEvent, actorUserId, occurredAtUtc, reason: null, predecessorCertificateId);
    }

    internal static CrewCertificateHistory RecordRevoked(
        Guid certificateId, Guid actorUserId, DateTime occurredAtUtc, string reason)
    {
        return new CrewCertificateHistory(
            Guid.NewGuid(), certificateId, RevokedEvent, actorUserId, occurredAtUtc, reason, predecessorCertificateId: null);
    }
}
