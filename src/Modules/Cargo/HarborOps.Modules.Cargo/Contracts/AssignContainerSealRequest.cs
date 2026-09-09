namespace HarborOps.Modules.Cargo.Contracts;

public sealed record AssignContainerSealRequest
{
    public string? SealNumber { get; init; }
}
