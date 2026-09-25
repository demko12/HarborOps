namespace HarborOps.Modules.Documents.Domain;

public sealed class DocumentDistributionRecipient
{
    public const string PendingState = "Pending";
    public const string AcknowledgedState = "Acknowledged";
    public const string CancelledState = "Cancelled";
    public const string ObsoleteState = "Obsolete";

    public Guid Id { get; private set; }
    public Guid IssueId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string State { get; private set; } = PendingState;
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTime? StateChangedAtUtc { get; private set; }
    public string? StateReason { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    private DocumentDistributionRecipient() { }
    public DocumentDistributionRecipient(Guid issueId, Guid recipientUserId)
    {
        Id = Guid.NewGuid(); IssueId = issueId; RecipientUserId = recipientUserId; ConcurrencyToken = Guid.NewGuid();
    }
    public void Acknowledge(Guid actorId, DateTime nowUtc)
    {
        if (State != PendingState) throw new InvalidOperationException("Only pending acknowledgements can be acknowledged.");
        State = AcknowledgedState; AcknowledgedAtUtc = nowUtc; AcknowledgedByUserId = actorId; StateChangedAtUtc = nowUtc; ConcurrencyToken = Guid.NewGuid();
    }
    public void Cancel(DateTime nowUtc, string reason) => ChangePendingState(CancelledState, nowUtc, reason);
    public void MarkObsolete(DateTime nowUtc, string reason) => ChangePendingState(ObsoleteState, nowUtc, reason);
    private void ChangePendingState(string state, DateTime nowUtc, string reason)
    {
        if (State != PendingState) return;
        State = state; StateChangedAtUtc = nowUtc; StateReason = reason; ConcurrencyToken = Guid.NewGuid();
    }
}
