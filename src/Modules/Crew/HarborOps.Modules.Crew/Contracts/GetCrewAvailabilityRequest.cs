namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewAvailabilityRequest
{
    public Guid? CrewMemberId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
}
