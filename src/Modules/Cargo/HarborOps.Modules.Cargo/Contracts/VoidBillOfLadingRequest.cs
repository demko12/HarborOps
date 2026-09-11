namespace HarborOps.Modules.Cargo.Contracts;

public sealed record VoidBillOfLadingRequest
{
    public string? Reason { get; init; }
}
