namespace HarborOps.Modules.Documents.Domain;

public sealed class Document
{
    public const string ActiveStatus = "Active";
    public const string SupersededStatus = "Superseded";
    public const string RevokedStatus = "Revoked";
    public const string DraftReviewStatus = "Draft";
    public const string PendingReviewReviewStatus = "PendingReview";
    public const string ApprovedReviewStatus = "Approved";
    public const string RejectedReviewStatus = "Rejected";
    internal const int NumberMaxLength = 100;
    internal const int IssuingAuthorityMaxLength = 200;
    internal const int StatusMaxLength = 50;
    internal const int FileNameMaxLength = 260;
    internal const int ContentTypeMaxLength = 255;
    internal const int StorageKeyMaxLength = 200;

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public int DocumentTypeId { get; private set; }

    public DocumentType DocumentType { get; private set; } = null!;

    public Guid? UploadedBy { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public DateOnly IssueDate { get; private set; }

    public DateOnly ExpiryDate { get; private set; }

    public string IssuingAuthority { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string ReviewStatus { get; private set; } = string.Empty;

    public Guid ConcurrencyToken { get; private set; }

    public Guid? PredecessorDocumentId { get; private set; }

    public Document? PredecessorDocument { get; private set; }

    public DateTime? SupersededAtUtc { get; private set; }

    public Guid? SupersededByUserId { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public string? FileName { get; private set; }

    public string? StorageKey { get; private set; }

    public string? ContentType { get; private set; }

    public long? SizeBytes { get; private set; }

    public DateTime? UploadedAtUtc { get; private set; }

    /// <summary>Changes whenever the downloadable binary changes.</summary>
    public Guid? FileRevisionId { get; private set; }

    public bool HasFile => StorageKey is not null;

    private Document()
    {
    }

    public Document(
        Guid vesselId,
        int documentTypeId,
        string number,
        DateOnly issueDate,
        DateOnly expiryDate,
        string issuingAuthority,
        string status)
    {
        Id = Guid.NewGuid();
        VesselId = vesselId;
        DocumentTypeId = documentTypeId;
        Number = number;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        IssuingAuthority = issuingAuthority;
        Status = status;
        ReviewStatus = DraftReviewStatus;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void AttachFile(
        string fileName,
        string contentType,
        string storageKey,
        long sizeBytes,
        Guid uploadedBy,
        DateTime uploadedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException("Size in bytes must be greater than zero.", nameof(sizeBytes));
        }

        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        UploadedBy = uploadedBy;
        UploadedAtUtc = uploadedAtUtc;
        FileRevisionId = Guid.NewGuid();
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void CorrectMetadata(int documentTypeId, string number, DateOnly issueDate, DateOnly expiryDate, string issuingAuthority)
    {
        DocumentTypeId = documentTypeId;
        Number = number;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        IssuingAuthority = issuingAuthority;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Supersede(Guid actorId, DateTime supersededAtUtc)
    {
        if (!string.Equals(Status, ActiveStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only an active document can be renewed.");
        }

        Status = SupersededStatus;
        SupersededByUserId = actorId;
        SupersededAtUtc = supersededAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void LinkToPredecessor(Guid predecessorDocumentId)
    {
        PredecessorDocumentId = predecessorDocumentId;
    }

    internal void SubmitForReview(Guid actorId, DateTimeOffset utcNow)
    {
        if (!HasFile)
        {
            throw new InvalidOperationException("A document must have an attached file before it can be submitted for review.");
        }

        if (!string.Equals(ReviewStatus, DraftReviewStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a draft document can be submitted for review.");
        }

        ReviewStatus = PendingReviewReviewStatus;
        ConcurrencyToken = Guid.NewGuid();
    }

    // Whether the reviewer differs from the submitter cannot be checked here: the
    // aggregate does not track who submitted the pending review. That comparison
    // requires the DocumentReviewEvent history and is enforced in the Application layer.
    internal void Approve(Guid reviewerId, DateTimeOffset utcNow)
    {
        if (!string.Equals(ReviewStatus, PendingReviewReviewStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a document pending review can be approved.");
        }

        ReviewStatus = ApprovedReviewStatus;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Reject(Guid reviewerId, string notes, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException("Notes are required when rejecting a document.", nameof(notes));
        }

        if (!string.Equals(ReviewStatus, PendingReviewReviewStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a document pending review can be rejected.");
        }

        ReviewStatus = RejectedReviewStatus;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void ReturnToDraftAfterCorrection()
    {
        if (!string.Equals(ReviewStatus, RejectedReviewStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a rejected document can be returned to draft.");
        }

        ReviewStatus = DraftReviewStatus;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Revoke(Guid actorId, string reason, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to revoke a document.", nameof(reason));
        }

        if (!string.Equals(Status, ActiveStatus, StringComparison.Ordinal) ||
            !string.Equals(ReviewStatus, ApprovedReviewStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only an active, approved document can be revoked.");
        }

        Status = RevokedStatus;
        RevokedAtUtc = utcNow.UtcDateTime;
        RevokedByUserId = actorId;
        ConcurrencyToken = Guid.NewGuid();
    }
}
