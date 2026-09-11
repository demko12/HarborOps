namespace HarborOps.Modules.Crew.Contracts;

public sealed record CancelCrewAvailabilityRequestRequest
{
    public string? Reason { get; init; }
}
