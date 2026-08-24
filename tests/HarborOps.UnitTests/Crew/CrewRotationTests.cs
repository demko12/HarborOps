using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewRotationTests
{
    [Fact]
    public void Update_replaces_relieving_member_pattern_and_planned_relief_date()
    {
        var rotation = CrewRotation.Create(
            Guid.NewGuid(), relievingCrewMemberId: null, "2-2", new DateOnly(2026, 3, 1));
        var newRelievingMemberId = Guid.NewGuid();
        var newPlannedReliefDate = new DateOnly(2026, 4, 1);

        rotation.Update(newRelievingMemberId, "4-4", newPlannedReliefDate);

        Assert.Equal(newRelievingMemberId, rotation.RelievingCrewMemberId);
        Assert.Equal("4-4", rotation.RotationPattern);
        Assert.Equal(newPlannedReliefDate, rotation.PlannedReliefDate);
    }

    [Fact]
    public void Update_can_clear_a_previously_selected_relieving_member()
    {
        var rotation = CrewRotation.Create(
            Guid.NewGuid(), Guid.NewGuid(), "2-2", new DateOnly(2026, 3, 1));

        rotation.Update(relievingCrewMemberId: null, "2-2", new DateOnly(2026, 3, 15));

        Assert.Null(rotation.RelievingCrewMemberId);
    }
}
