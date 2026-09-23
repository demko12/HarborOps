namespace HarborOps.Modules.Crew.Domain;

internal sealed class VesselManningPlanLine
{
    private VesselManningPlanLine()
    {
    }

    private VesselManningPlanLine(
        Guid id,
        Guid vesselManningPlanId,
        Guid rankId,
        int requiredHeadcount)
    {
        Id = id;
        VesselManningPlanId = vesselManningPlanId;
        RankId = rankId;
        RequiredHeadcount = requiredHeadcount;
    }

    public Guid Id { get; private set; }
    public Guid VesselManningPlanId { get; private set; }
    public Guid RankId { get; private set; }
    public int RequiredHeadcount { get; private set; }

    internal static VesselManningPlanLine Create(Guid vesselManningPlanId, Guid rankId, int requiredHeadcount)
    {
        if (requiredHeadcount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredHeadcount), requiredHeadcount, "Required headcount must be greater than zero.");
        }

        return new VesselManningPlanLine(Guid.NewGuid(), vesselManningPlanId, rankId, requiredHeadcount);
    }

    internal void ChangeRequiredHeadcount(int requiredHeadcount)
    {
        if (requiredHeadcount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredHeadcount), requiredHeadcount, "Required headcount must be greater than zero.");
        }

        RequiredHeadcount = requiredHeadcount;
    }
}
