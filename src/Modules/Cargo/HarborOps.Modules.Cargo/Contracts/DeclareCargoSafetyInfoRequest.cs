namespace HarborOps.Modules.Cargo.Contracts;

public sealed record DeclareCargoSafetyInfoRequest
{
    public string? ImdgClass { get; init; }

    public string? UnNumber { get; init; }

    public string? StorageConditions { get; init; }

    public string? HandlingNotes { get; init; }
}
