namespace HarborOps.Modules.Crew.Domain;

internal sealed class VesselManningPlanHistory
{
    internal const int EventTypeMaxLength = 20;
    internal const int ReasonMaxLength = 500;
    internal const int LinesSnapshotMaxLength = 4000;

    internal const string CreatedEvent = "Created";
    internal const string LinesReplacedEvent = "LinesReplaced";
    internal const string EndDateChangedEvent = "EndDateChanged";

    private VesselManningPlanHistory()
    {
    }

    private VesselManningPlanHistory(
        Guid id,
        Guid vesselManningPlanId,
        string eventType,
        string? reason,
        Guid actorUserId,
        DateTime occurredAtUtc,
        DateOnly? previousEffectiveEndDate,
        DateOnly? newEffectiveEndDate,
        string? previousLinesSnapshot,
        string? newLinesSnapshot)
    {
        Id = id;
        VesselManningPlanId = vesselManningPlanId;
        EventType = eventType;
        Reason = reason;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        PreviousEffectiveEndDate = previousEffectiveEndDate;
        NewEffectiveEndDate = newEffectiveEndDate;
        PreviousLinesSnapshot = previousLinesSnapshot;
        NewLinesSnapshot = newLinesSnapshot;
    }

    public Guid Id { get; private set; }
    public Guid VesselManningPlanId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string? Reason { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateOnly? PreviousEffectiveEndDate { get; private set; }
    public DateOnly? NewEffectiveEndDate { get; private set; }
    public string? PreviousLinesSnapshot { get; private set; }
    public string? NewLinesSnapshot { get; private set; }

    internal static VesselManningPlanHistory RecordCreated(
        Guid vesselManningPlanId,
        Guid actorUserId,
        DateTime occurredAtUtc,
        string newLinesSnapshot)
    {
        return new VesselManningPlanHistory(
            Guid.NewGuid(),
            vesselManningPlanId,
            CreatedEvent,
            reason: null,
            actorUserId,
            occurredAtUtc,
            previousEffectiveEndDate: null,
            newEffectiveEndDate: null,
            previousLinesSnapshot: null,
            newLinesSnapshot);
    }

    internal static VesselManningPlanHistory RecordLinesReplaced(
        Guid vesselManningPlanId,
        Guid actorUserId,
        string reason,
        DateTime occurredAtUtc,
        string previousLinesSnapshot,
        string newLinesSnapshot)
    {
        return new VesselManningPlanHistory(
            Guid.NewGuid(),
            vesselManningPlanId,
            LinesReplacedEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            previousEffectiveEndDate: null,
            newEffectiveEndDate: null,
            previousLinesSnapshot,
            newLinesSnapshot);
    }

    internal static VesselManningPlanHistory RecordEndDateChanged(
        Guid vesselManningPlanId,
        Guid actorUserId,
        string reason,
        DateTime occurredAtUtc,
        DateOnly previousEndDate,
        DateOnly newEndDate)
    {
        return new VesselManningPlanHistory(
            Guid.NewGuid(),
            vesselManningPlanId,
            EndDateChangedEvent,
            reason,
            actorUserId,
            occurredAtUtc,
            previousEndDate,
            newEndDate,
            previousLinesSnapshot: null,
            newLinesSnapshot: null);
    }
}
