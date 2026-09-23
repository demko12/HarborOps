using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class VesselManningPlanLineTests
{
    [Fact]
    public void Create_sets_fields()
    {
        var planId = Guid.NewGuid();
        var rankId = Guid.NewGuid();

        var line = VesselManningPlanLine.Create(planId, rankId, 3);

        Assert.Equal(planId, line.VesselManningPlanId);
        Assert.Equal(rankId, line.RankId);
        Assert.Equal(3, line.RequiredHeadcount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_non_positive_required_headcount(int requiredHeadcount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VesselManningPlanLine.Create(Guid.NewGuid(), Guid.NewGuid(), requiredHeadcount));
    }

    [Fact]
    public void ChangeRequiredHeadcount_updates_the_value()
    {
        var line = VesselManningPlanLine.Create(Guid.NewGuid(), Guid.NewGuid(), 2);

        line.ChangeRequiredHeadcount(5);

        Assert.Equal(5, line.RequiredHeadcount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChangeRequiredHeadcount_rejects_a_non_positive_value(int requiredHeadcount)
    {
        var line = VesselManningPlanLine.Create(Guid.NewGuid(), Guid.NewGuid(), 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => line.ChangeRequiredHeadcount(requiredHeadcount));
    }
}
