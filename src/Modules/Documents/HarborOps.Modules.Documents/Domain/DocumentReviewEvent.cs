namespace HarborOps.Modules.Documents.Domain;

public sealed class DocumentReviewEvent
{
    public const string SubmittedAction = "Submitted";
    public const string ApprovedAction = "Approved";
    public const string RejectedAction = "Rejected";
    public const string RevokedAction = "Revoked";

    internal const int NotesMaxLength = 2000;

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Document Document { get; private set; } = null!;

    public string Action { get; private set; } = string.Empty;

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string? Notes { get; private set; }

    private DocumentReviewEvent()
    {
    }

    public DocumentReviewEvent(Guid documentId, string action, Guid actorUserId, DateTimeOffset occurredAtUtc, string? notes)
    {
        if (action is not (SubmittedAction or ApprovedAction or RejectedAction or RevokedAction))
        {
            throw new ArgumentException($"'{action}' is not a recognized document review action.", nameof(action));
        }

        if (action is RejectedAction or RevokedAction && string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException($"Notes are required for the '{action}' action.", nameof(notes));
        }

        Id = Guid.NewGuid();
        DocumentId = documentId;
        Action = action;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        Notes = notes;
    }
}
