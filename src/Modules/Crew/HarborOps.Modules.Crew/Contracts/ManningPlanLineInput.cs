namespace HarborOps.Modules.Crew.Contracts;

public sealed record ManningPlanLineInput
{
    public Guid? RankId { get; init; }
    public int? RequiredHeadcount { get; init; }
}
