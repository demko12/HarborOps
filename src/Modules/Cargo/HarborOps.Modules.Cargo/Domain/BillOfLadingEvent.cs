namespace HarborOps.Modules.Cargo.Domain;

internal sealed class BillOfLadingEvent
{
    internal const int ActionMaxLength = 50;
    internal const int NotesMaxLength = 1000;
    internal const int StatusMaxLength = 50;

    private BillOfLadingEvent()
    {
    }

    private BillOfLadingEvent(
        Guid id,
        Guid billOfLadingId,
        BillOfLadingAction action,
        BillOfLadingStatus fromStatus,
        BillOfLadingStatus toStatus,
        string? notes,
        Guid actorUserId,
        DateTime occurredUtc)
    {
        Id = id;
        BillOfLadingId = billOfLadingId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Notes = notes;
        ActorUserId = actorUserId;
        OccurredUtc = occurredUtc;
    }

    public Guid Id { get; private set; }

    public Guid BillOfLadingId { get; private set; }

    public BillOfLadingAction Action { get; private set; }

    public BillOfLadingStatus FromStatus { get; private set; }

    public BillOfLadingStatus ToStatus { get; private set; }

    public string? Notes { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    internal static BillOfLadingEvent Record(
        Guid billOfLadingId,
        BillOfLadingAction action,
        BillOfLadingStatus fromStatus,
        BillOfLadingStatus toStatus,
        Guid actorUserId,
        DateTime occurredUtc,
        string? notes = null)
    {
        return new BillOfLadingEvent(
            Guid.NewGuid(),
            billOfLadingId,
            action,
            fromStatus,
            toStatus,
            notes,
            actorUserId,
            occurredUtc);
    }
}
