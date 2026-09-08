namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ReleaseContainerRequest
{
    public string? Notes { get; init; }
}
