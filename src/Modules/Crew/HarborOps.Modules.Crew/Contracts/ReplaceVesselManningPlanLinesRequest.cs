namespace HarborOps.Modules.Crew.Contracts;

public sealed record ReplaceVesselManningPlanLinesRequest
{
    public IReadOnlyList<ManningPlanLineInput>? Lines { get; init; }
    public string? Reason { get; init; }
}
