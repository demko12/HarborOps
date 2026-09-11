namespace HarborOps.Modules.Documents.Domain;

public sealed class LegalHold
{
    internal const int ReasonMaxLength = 2000;
    internal const int ReferenceMaxLength = 200;
    internal const int ReleaseReasonMaxLength = 2000;

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public string? Reference { get; private set; }

    public Guid PlacedByUserId { get; private set; }

    public DateTime PlacedAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public string? ReleaseReason { get; private set; }

    public Guid? ReleasedByUserId { get; private set; }

    public DateTime? ReleasedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    private LegalHold()
    {
    }

    private LegalHold(Guid documentId, string reason, string? reference, Guid actorId, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        DocumentId = documentId;
        Reason = reason;
        Reference = reference;
        PlacedByUserId = actorId;
        PlacedAtUtc = nowUtc;
        IsActive = true;
        ConcurrencyToken = Guid.NewGuid();
    }

    public static LegalHold Place(Guid documentId, string reason, string? reference, Guid actorId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to place a legal hold.", nameof(reason));
        }

        return new LegalHold(
            documentId,
            reason.Trim(),
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            actorId,
            nowUtc);
    }

    public void Release(Guid actorId, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to release a legal hold.", nameof(reason));
        }

        if (!IsActive)
        {
            throw new InvalidOperationException("Only an active legal hold can be released.");
        }

        IsActive = false;
        ReleaseReason = reason.Trim();
        ReleasedByUserId = actorId;
        ReleasedAtUtc = nowUtc;
        ConcurrencyToken = Guid.NewGuid();
    }
}
