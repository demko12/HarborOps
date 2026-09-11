namespace HarborOps.Modules.Crew.Contracts;

public sealed record CreateCrewAvailabilityRequestRequest
{
    public string? Type { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Reason { get; init; }
    public string? Notes { get; init; }
}
