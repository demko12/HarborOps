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
}
