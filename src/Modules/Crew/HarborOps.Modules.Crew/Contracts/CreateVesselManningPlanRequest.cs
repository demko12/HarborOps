namespace HarborOps.Modules.Crew.Contracts;

public sealed record CreateVesselManningPlanRequest
{
    public Guid? VesselId { get; init; }
    public DateOnly? EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public IReadOnlyList<ManningPlanLineInput>? Lines { get; init; }
}
