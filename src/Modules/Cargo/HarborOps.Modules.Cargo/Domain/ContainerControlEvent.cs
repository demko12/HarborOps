namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ContainerControlEvent
{
    internal const int ActionMaxLength = 50;
    internal const int InspectionResultMaxLength = 50;
    internal const int NotesMaxLength = 1000;
    internal const int StateMaxLength = 50;

    private ContainerControlEvent()
    {
    }

    private ContainerControlEvent(
        Guid id,
        Guid containerControlId,
        ContainerControlAction action,
        ContainerControlState fromState,
        ContainerControlState toState,
        string? sealNumber,
        ContainerInspectionResult? inspectionResult,
        decimal? temperatureCelsius,
        string? notes,
        Guid actorUserId,
        DateTime occurredUtc)
    {
        Id = id;
        ContainerControlId = containerControlId;
        Action = action;
        FromState = fromState;
        ToState = toState;
        SealNumber = sealNumber;
        InspectionResult = inspectionResult;
        TemperatureCelsius = temperatureCelsius;
        Notes = notes;
        ActorUserId = actorUserId;
        OccurredUtc = occurredUtc;
    }

    public Guid Id { get; private set; }

    public Guid ContainerControlId { get; private set; }

    public ContainerControlAction Action { get; private set; }

    public ContainerControlState FromState { get; private set; }

    public ContainerControlState ToState { get; private set; }

    public string? SealNumber { get; private set; }

    public ContainerInspectionResult? InspectionResult { get; private set; }

    public decimal? TemperatureCelsius { get; private set; }

    public string? Notes { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    internal static ContainerControlEvent Record(
        Guid containerControlId,
        ContainerControlAction action,
        ContainerControlState fromState,
        ContainerControlState toState,
        Guid actorUserId,
        DateTime occurredUtc,
        string? sealNumber = null,
        ContainerInspectionResult? inspectionResult = null,
        decimal? temperatureCelsius = null,
        string? notes = null)
    {
        return new ContainerControlEvent(
            Guid.NewGuid(),
            containerControlId,
            action,
            fromState,
            toState,
            sealNumber,
            inspectionResult,
            temperatureCelsius,
            notes,
            actorUserId,
            occurredUtc);
    }
}
