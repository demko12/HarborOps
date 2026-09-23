namespace HarborOps.Modules.Voyages.Contracts;

public sealed record FinalizePortCallStatementRequest
{
    public Guid? Version { get; init; }

    public string? SourceVersion { get; init; }
}
