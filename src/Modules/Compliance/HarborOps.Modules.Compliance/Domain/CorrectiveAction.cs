namespace HarborOps.Modules.Compliance.Domain;

public sealed class CorrectiveAction
{
    internal const int ActionTextMaxLength = 2000;

    public Guid Id { get; private set; }

    public int NonConformityId { get; private set; }

    public string ActionText { get; private set; } = string.Empty;

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public Guid? EvidenceDocumentId { get; private set; }

    private CorrectiveAction()
    {
    }

    private CorrectiveAction(
        Guid id,
        int nonConformityId,
        string actionText,
        Guid actorUserId,
        DateTimeOffset recordedAtUtc,
        Guid? evidenceDocumentId)
    {
        Id = id;
        NonConformityId = nonConformityId;
        ActionText = actionText;
        ActorUserId = actorUserId;
        RecordedAtUtc = recordedAtUtc;
        EvidenceDocumentId = evidenceDocumentId;
    }

    internal static CorrectiveAction Record(
        int nonConformityId,
        string actionText,
        Guid actorUserId,
        DateTimeOffset recordedAtUtc,
        Guid? evidenceDocumentId)
    {
        return new CorrectiveAction(
            Guid.NewGuid(),
            nonConformityId,
            actionText,
            actorUserId,
            recordedAtUtc,
            evidenceDocumentId);
    }
}
