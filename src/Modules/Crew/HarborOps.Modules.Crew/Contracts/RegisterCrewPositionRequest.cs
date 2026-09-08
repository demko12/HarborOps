namespace HarborOps.Modules.Crew.Contracts;

public sealed record RegisterCrewPositionRequest
{
    public string? RankName { get; init; }
    public string? Department { get; init; }
    public string? SeniorityLevel { get; init; }
}
