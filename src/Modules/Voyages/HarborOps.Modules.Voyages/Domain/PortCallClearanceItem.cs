namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceItem
{
    internal const int TitleMaxLength = 200;
    internal const int AuthorityReferenceMaxLength = 100;
    internal const int DecisionReasonMaxLength = 200;
    internal const int StatusMaxLength = 30;

    internal const string PendingStatus = "Pending";
    internal const string ClearedStatus = "Cleared";
    internal const string WaivedStatus = "Waived";

    private PortCallClearanceItem()
    {
    }

    private PortCallClearanceItem(
        Guid id,
        Guid portCallClearanceId,
        string category,
        string title,
        bool isRequired,
        DateTime? dueAtUtc,
        string? authorityReference,
        Guid? evidenceDocumentId,
        string status,
        Guid addedByUserId,
        DateTime addedAtUtc)
    {
        Id = id;
        PortCallClearanceId = portCallClearanceId;
        Category = category;
        Title = title;
        IsRequired = isRequired;
        DueAtUtc = dueAtUtc;
        AuthorityReference = authorityReference;
        EvidenceDocumentId = evidenceDocumentId;
        Status = status;
        AddedByUserId = addedByUserId;
        AddedAtUtc = addedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PortCallClearanceId { get; private set; }

    public string Category { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public bool IsRequired { get; private set; }

    public DateTime? DueAtUtc { get; private set; }

    public string? AuthorityReference { get; private set; }

    public Guid? EvidenceDocumentId { get; private set; }

    public string Status { get; private set; } = null!;

    public string? DecisionReason { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    public Guid AddedByUserId { get; private set; }

    public DateTime AddedAtUtc { get; private set; }

    internal bool IsPending => string.Equals(Status, PendingStatus, StringComparison.Ordinal);

    internal static PortCallClearanceItem Add(
        Guid portCallClearanceId,
        string category,
        string title,
        bool isRequired,
        DateTime? dueAtUtc,
        string? authorityReference,
        Guid? evidenceDocumentId,
        Guid addedByUserId,
        DateTime addedAtUtc)
    {
        return new PortCallClearanceItem(
            Guid.NewGuid(),
            portCallClearanceId,
            category,
            title,
            isRequired,
            dueAtUtc,
            authorityReference,
            evidenceDocumentId,
            PendingStatus,
            addedByUserId,
            addedAtUtc);
    }

    internal void Edit(
        string category,
        string title,
        bool isRequired,
        DateTime? dueAtUtc,
        string? authorityReference,
        Guid? evidenceDocumentId)
    {
        EnsurePending();

        Category = category;
        Title = title;
        IsRequired = isRequired;
        DueAtUtc = dueAtUtc;
        AuthorityReference = authorityReference;
        EvidenceDocumentId = evidenceDocumentId;
    }

    internal void Clear(
        string? authorityReference,
        Guid? evidenceDocumentId,
        Guid decidedByUserId,
        DateTime decidedAtUtc)
    {
        EnsurePending();

        if (authorityReference is not null)
        {
            AuthorityReference = authorityReference;
        }

        if (evidenceDocumentId is not null)
        {
            EvidenceDocumentId = evidenceDocumentId;
        }

        Status = ClearedStatus;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
    }

    internal void Waive(string reason, Guid decidedByUserId, DateTime decidedAtUtc)
    {
        EnsurePending();

        Status = WaivedStatus;
        DecisionReason = reason;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
    }

    private void EnsurePending()
    {
        if (!IsPending)
        {
            throw new PortCallClearanceItemNotPendingException(Id, Status);
        }
    }
}
