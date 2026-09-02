namespace HarborOps.Modules.Crew.Contracts;

public sealed record ChangeCrewMemberEmploymentStatusRequest
{
    public string? Status { get; init; }
    public string? Notes { get; init; }
}
