namespace HarborOps.Modules.Crew.Contracts;

public sealed record RejectCrewAvailabilityRequestRequest
{
    public string? Reason { get; init; }
}
