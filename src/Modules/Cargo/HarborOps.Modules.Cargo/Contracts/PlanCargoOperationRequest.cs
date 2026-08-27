namespace HarborOps.Modules.Cargo.Contracts;

public sealed record PlanCargoOperationRequest
{
    public string? OperationType { get; init; }

    public DateTime? PlannedAtUtc { get; init; }
}
