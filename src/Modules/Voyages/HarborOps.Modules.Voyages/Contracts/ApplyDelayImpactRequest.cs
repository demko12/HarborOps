namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ApplyDelayImpactRequest
{
    public string? Reason { get; init; }

    public IReadOnlyList<ApplyDelayImpactStopRequest>? Stops { get; init; }
}
