namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoidPortCallActivityRequest
{
    public string? Reason { get; init; }

    public Guid? Version { get; init; }
}
