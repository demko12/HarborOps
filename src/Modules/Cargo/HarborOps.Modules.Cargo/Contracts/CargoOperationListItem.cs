namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoOperationListItem(
    Guid OperationId,
    string OperationType,
    string OperationState,
    DateTime PlannedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    Guid ResponsibleUserId);
