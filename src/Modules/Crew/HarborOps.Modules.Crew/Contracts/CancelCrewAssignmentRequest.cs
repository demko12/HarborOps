namespace HarborOps.Modules.Crew.Contracts;

public sealed record CancelCrewAssignmentRequest
{
    public string? Notes { get; init; }
}
