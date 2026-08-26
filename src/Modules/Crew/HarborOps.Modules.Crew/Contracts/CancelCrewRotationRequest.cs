namespace HarborOps.Modules.Crew.Contracts;

public sealed record CancelCrewRotationRequest
{
    public string? Notes { get; init; }
}
