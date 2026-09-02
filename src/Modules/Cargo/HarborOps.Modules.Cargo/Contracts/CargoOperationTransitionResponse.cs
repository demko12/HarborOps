namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoOperationTransitionResponse(
    Guid OperationId,
    string OperationState,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc);
