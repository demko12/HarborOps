namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewManningCoverageRequest
{
    public Guid? VesselId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}
