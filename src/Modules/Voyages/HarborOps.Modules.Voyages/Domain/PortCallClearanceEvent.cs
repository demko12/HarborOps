namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceEvent
{
    internal const int EventTypeMaxLength = 30;
    internal const int ReasonMaxLength = 200;

    internal const string ItemAddedEventType = "ItemAdded";
    internal const string ItemUpdatedEventType = "ItemUpdated";
    internal const string ItemClearedEventType = "ItemCleared";
    internal const string ItemWaivedEventType = "ItemWaived";
    internal const string CompletedEventType = "Completed";
    internal const string ReopenedEventType = "Reopened";

    private PortCallClearanceEvent()
    {
    }

    private PortCallClearanceEvent(
        Guid id,
        Guid portCallClearanceId,
        Guid? portCallClearanceItemId,
        int sequence,
        string eventType,
        string? reason,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        Id = id;
        PortCallClearanceId = portCallClearanceId;
        PortCallClearanceItemId = portCallClearanceItemId;
        Sequence = sequence;
        EventType = eventType;
        Reason = reason;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PortCallClearanceId { get; private set; }

    public int Sequence { get; private set; }

    public Guid? PortCallClearanceItemId { get; private set; }

    public string EventType { get; private set; } = null!;

    public string? Reason { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    internal static PortCallClearanceEvent Record(
        Guid portCallClearanceId,
        Guid? portCallClearanceItemId,
        int sequence,
        string eventType,
        string? reason,
        Guid actorUserId,
        DateTime occurredAtUtc)
    {
        return new PortCallClearanceEvent(
            Guid.NewGuid(),
            portCallClearanceId,
            portCallClearanceItemId,
            sequence,
            eventType,
            reason,
            actorUserId,
            occurredAtUtc);
    }
}
