namespace HarborOps.Modules.Documents.Domain;

public sealed class DispositionRequest
{
    public const string PendingStatus = "Pending";
    public const string ApprovedStatus = "Approved";
    public const string RejectedStatus = "Rejected";

    internal const int ReasonMaxLength = 2000;
    internal const int DecisionReasonMaxLength = 2000;
    internal const int StatusMaxLength = 50;

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public Guid? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    public string? DecisionReason { get; private set; }

    public int SnapshotRetentionPeriodDays { get; private set; }

    public DateTime SnapshotLifecycleTriggerAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    private DispositionRequest()
    {
    }

    private DispositionRequest(
        Guid documentId,
        Guid requestedByUserId,
        string reason,
        DateTime nowUtc,
        int snapshotRetentionPeriodDays,
        DateTime snapshotLifecycleTriggerAtUtc)
    {
        Id = Guid.NewGuid();
        DocumentId = documentId;
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = nowUtc;
        Reason = reason;
        Status = PendingStatus;
        SnapshotRetentionPeriodDays = snapshotRetentionPeriodDays;
        SnapshotLifecycleTriggerAtUtc = snapshotLifecycleTriggerAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static DispositionRequest Create(
        Guid documentId,
        Guid requestedByUserId,
        string reason,
        DateTime nowUtc,
        int snapshotRetentionPeriodDays,
        DateTime snapshotLifecycleTriggerAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to request disposition.", nameof(reason));
        }

        if (snapshotRetentionPeriodDays <= 0)
        {
            throw new ArgumentException("Snapshot retention period must be greater than zero days.", nameof(snapshotRetentionPeriodDays));
        }

        return new DispositionRequest(
            documentId,
            requestedByUserId,
            reason.Trim(),
            nowUtc,
            snapshotRetentionPeriodDays,
            snapshotLifecycleTriggerAtUtc);
    }

    public void Approve(Guid actorId, string? decisionReason, DateTime nowUtc)
    {
        if (actorId == RequestedByUserId)
        {
            throw new InvalidOperationException("The approver must be different from the requester.");
        }

        EnsurePending();

        Status = ApprovedStatus;
        DecidedByUserId = actorId;
        DecidedAtUtc = nowUtc;
        DecisionReason = string.IsNullOrWhiteSpace(decisionReason) ? null : decisionReason.Trim();
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Reject(Guid actorId, string decisionReason, DateTime nowUtc)
    {
        if (actorId == RequestedByUserId)
        {
            throw new InvalidOperationException("The approver must be different from the requester.");
        }

        EnsurePending();

        if (string.IsNullOrWhiteSpace(decisionReason))
        {
            throw new ArgumentException("A decision reason is required to reject a disposition request.", nameof(decisionReason));
        }

        Status = RejectedStatus;
        DecidedByUserId = actorId;
        DecidedAtUtc = nowUtc;
        DecisionReason = decisionReason.Trim();
        ConcurrencyToken = Guid.NewGuid();
    }

    private void EnsurePending()
    {
        if (!string.Equals(Status, PendingStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a pending disposition request can be decided.");
        }
    }
}
