namespace HarborOps.Modules.Compliance.Domain;

internal sealed class ComplianceCertificateHistory
{
    internal const string RenewedEvent = "Renewed";
    internal const string RevokedEvent = "Revoked";

    internal const int EventTypeMaxLength = 30;

    private ComplianceCertificateHistory()
    {
    }

    private ComplianceCertificateHistory(
        Guid id,
        Guid certificateId,
        string eventType,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string? reason,
        Guid? successorCertificateId)
    {
        Id = id;
        CertificateId = certificateId;
        EventType = eventType;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        Reason = reason;
        SuccessorCertificateId = successorCertificateId;
    }

    public Guid Id { get; private set; }

    public Guid CertificateId { get; private set; }

    public string EventType { get; private set; } = null!;

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string? Reason { get; private set; }

    public Guid? SuccessorCertificateId { get; private set; }

    internal static ComplianceCertificateHistory RecordRenewed(
        Guid certificateId,
        Guid successorCertificateId,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        return new ComplianceCertificateHistory(
            Guid.NewGuid(),
            certificateId,
            RenewedEvent,
            actorUserId,
            occurredAtUtc,
            reason: null,
            successorCertificateId);
    }

    internal static ComplianceCertificateHistory RecordRevoked(
        Guid certificateId,
        string reason,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        return new ComplianceCertificateHistory(
            Guid.NewGuid(),
            certificateId,
            RevokedEvent,
            actorUserId,
            occurredAtUtc,
            reason,
            successorCertificateId: null);
    }
}
