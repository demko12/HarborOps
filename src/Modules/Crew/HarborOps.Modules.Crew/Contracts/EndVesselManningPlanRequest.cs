namespace HarborOps.Modules.Crew.Contracts;

public sealed record EndVesselManningPlanRequest
{
    public DateOnly? NewEndDate { get; init; }
    public string? Reason { get; init; }
}
