namespace HarborOps.Modules.Crew.Contracts;

public sealed record SearchCrewMembersRequest
{
    public string? Search { get; init; }

    public string? EmploymentStatus { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
