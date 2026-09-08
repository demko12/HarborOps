namespace HarborOps.Modules.Cargo.Contracts;

public sealed record HoldContainerRequest
{
    public string? Reason { get; init; }
}
