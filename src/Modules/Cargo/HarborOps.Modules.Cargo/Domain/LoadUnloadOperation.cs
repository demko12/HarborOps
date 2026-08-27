namespace HarborOps.Modules.Cargo.Domain;

internal sealed class LoadUnloadOperation
{
    internal const int OperationTypeMaxLength = 50;
    internal const int StatusMaxLength = 50;

    private LoadUnloadOperation()
    {
    }

    private LoadUnloadOperation(
        Guid id,
        Guid cargoBookingId,
        Guid responsibleUserId,
        CargoOperationType operationType,
        CargoStatus status,
        DateTime plannedAtUtc)
    {
        Id = id;
        CargoBookingId = cargoBookingId;
        ResponsibleUserId = responsibleUserId;
        OperationType = operationType;
        Status = status;
        PlannedAtUtc = plannedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid CargoBookingId { get; private set; }

    public Guid ResponsibleUserId { get; private set; }

    public CargoOperationType OperationType { get; private set; }

    public CargoStatus Status { get; private set; }

    public DateTime PlannedAtUtc { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    internal CargoOperationState State => StateFor(StartedAtUtc, FinishedAtUtc);

    internal static CargoOperationState StateFor(DateTime? startedAtUtc, DateTime? finishedAtUtc)
    {
        if (finishedAtUtc is not null)
        {
            return CargoOperationState.Completed;
        }

        return startedAtUtc is not null
            ? CargoOperationState.InProgress
            : CargoOperationState.Planned;
    }

    internal static CargoStatus OutcomeStatusFor(CargoOperationType operationType)
    {
        return operationType switch
        {
            CargoOperationType.Load => CargoStatus.Loaded,
            _ => CargoStatus.Unloaded,
        };
    }

    internal static CargoStatus RequiredBookingStatusFor(CargoOperationType operationType)
    {
        return operationType switch
        {
            CargoOperationType.Load => CargoStatus.Planned,
            _ => CargoStatus.Arrived,
        };
    }

    internal static CargoStatus RequiredContainerStatusFor(CargoOperationType operationType)
    {
        return operationType switch
        {
            CargoOperationType.Load => CargoStatus.Registered,
            _ => CargoStatus.Loaded,
        };
    }

    internal static LoadUnloadOperation Plan(
        Guid cargoBookingId,
        Guid responsibleUserId,
        CargoOperationType operationType,
        DateTime plannedAtUtc)
    {
        return new LoadUnloadOperation(
            Guid.NewGuid(),
            cargoBookingId,
            responsibleUserId,
            operationType,
            CargoStatus.Planned,
            plannedAtUtc);
    }

    internal void Start(DateTime utcNow)
    {
        if (State is not CargoOperationState.Planned)
        {
            throw new LoadUnloadOperationTransitionNotAllowedException(
                Id,
                State,
                LoadUnloadOperationTransitionNotAllowedException.StartAction);
        }

        StartedAtUtc = utcNow;
    }

    internal void Complete(DateTime utcNow)
    {
        if (State is not CargoOperationState.InProgress)
        {
            throw new LoadUnloadOperationTransitionNotAllowedException(
                Id,
                State,
                LoadUnloadOperationTransitionNotAllowedException.CompleteAction);
        }

        FinishedAtUtc = utcNow;
        Status = OutcomeStatusFor(OperationType);
    }
}
