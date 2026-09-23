namespace HarborOps.Modules.Compliance.Domain;

public sealed class PscDetentionDecision
{
    internal const string DetainedDecision = "Detained";
    internal const string ReleasedDecision = "Released";
    internal const int DecisionMaxLength = 20;
    internal const int AuthorityReferenceMaxLength = 200;
    internal const int ReasonMaxLength = 1000;

    public Guid Id { get; private set; }

    public int PscInspectionId { get; private set; }

    public string Decision { get; private set; } = string.Empty;

    public string AuthorityReference { get; private set; } = string.Empty;

    public string? Reason { get; private set; }

    public DateOnly? ReleaseDate { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    private PscDetentionDecision()
    {
    }

    private PscDetentionDecision(
        Guid id,
        int pscInspectionId,
        string decision,
        string authorityReference,
        string? reason,
        DateOnly? releaseDate,
        Guid? documentId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc)
    {
        Id = id;
        PscInspectionId = pscInspectionId;
        Decision = decision;
        AuthorityReference = authorityReference;
        Reason = reason;
        ReleaseDate = releaseDate;
        DocumentId = documentId;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
    }

    internal static PscDetentionDecision RecordDetained(
        int pscInspectionId,
        string authorityReference,
        string reason,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc) =>
        new(Guid.NewGuid(), pscInspectionId, DetainedDecision, authorityReference, reason, null, null, actorUserId, occurredAtUtc);

    internal static PscDetentionDecision RecordReleased(
        int pscInspectionId,
        string authorityReference,
        DateOnly releaseDate,
        Guid? documentId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc) =>
        new(Guid.NewGuid(), pscInspectionId, ReleasedDecision, authorityReference, null, releaseDate, documentId, actorUserId, occurredAtUtc);
}
