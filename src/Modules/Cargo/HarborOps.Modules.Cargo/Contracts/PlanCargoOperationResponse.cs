namespace HarborOps.Modules.Cargo.Contracts;

public sealed record PlanCargoOperationResponse(
    Guid Id,
    string OperationType,
    string OperationState);
