namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancyEvent
{
    internal const int EventTypeMaxLength = 50;
    internal const int StatusMaxLength = 50;
    internal const int TextMaxLength = 2000;

    private CargoDiscrepancyEvent()
    {
    }

    private CargoDiscrepancyEvent(
        Guid id,
        Guid discrepancyId,
        CargoDiscrepancyEventType eventType,
        CargoDiscrepancyStatus? fromStatus,
        CargoDiscrepancyStatus? toStatus,
        string? text,
        Guid actorUserId,
        string? actorName,
        DateTime occurredAtUtc)
    {
        Id = id;
        DiscrepancyId = discrepancyId;
        EventType = eventType;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Text = text;
        ActorUserId = actorUserId;
        ActorName = actorName;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid DiscrepancyId { get; private set; }

    public CargoDiscrepancyEventType EventType { get; private set; }

    public CargoDiscrepancyStatus? FromStatus { get; private set; }

    public CargoDiscrepancyStatus? ToStatus { get; private set; }

    public string? Text { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string? ActorName { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    internal static CargoDiscrepancyEvent Record(
        Guid discrepancyId,
        CargoDiscrepancyEventType eventType,
        CargoDiscrepancyStatus? fromStatus,
        CargoDiscrepancyStatus? toStatus,
        Guid actorUserId,
        string? actorName,
        DateTime occurredAtUtc,
        string? text = null)
    {
        return new CargoDiscrepancyEvent(
            Guid.NewGuid(),
            discrepancyId,
            eventType,
            fromStatus,
            toStatus,
            text,
            actorUserId,
            actorName,
            occurredAtUtc);
    }
}
