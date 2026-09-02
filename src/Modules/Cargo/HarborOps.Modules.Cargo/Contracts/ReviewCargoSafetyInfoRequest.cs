namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ReviewCargoSafetyInfoRequest
{
    public string? Status { get; init; }

    public string? Notes { get; init; }
}
