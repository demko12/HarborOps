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

    [Fact]
    public void Execute_retains_its_auditable_outcome()
    {
        var rotation = CrewRotation.Create(Guid.NewGuid(), Guid.NewGuid(), "2-2", new DateOnly(2026, 3, 1));
        var actorId = Guid.NewGuid();
        var replacementAssignmentId = Guid.NewGuid();
        var executedAtUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        rotation.Execute(actorId, executedAtUtc, replacementAssignmentId);

        Assert.Equal(CrewRotation.ExecutedStatus, rotation.Status);
        Assert.Equal(actorId, rotation.ExecutedByUserId);
        Assert.Equal(executedAtUtc, rotation.ExecutedAtUtc);
        Assert.Equal(replacementAssignmentId, rotation.ReplacementAssignmentId);
        Assert.Throws<InvalidCrewRotationTransitionException>(() => rotation.Cancel(actorId, executedAtUtc, null));
    }

    [Fact]
    public void Cancel_retains_its_auditable_outcome()
    {
        var rotation = CrewRotation.Create(Guid.NewGuid(), null, "2-2", new DateOnly(2026, 3, 1));
        var actorId = Guid.NewGuid();
        var cancelledAtUtc = new DateTime(2026, 2, 20, 8, 0, 0, DateTimeKind.Utc);

        rotation.Cancel(actorId, cancelledAtUtc, "Relief delayed");

        Assert.Equal(CrewRotation.CancelledStatus, rotation.Status);
        Assert.Equal(actorId, rotation.CancelledByUserId);
        Assert.Equal(cancelledAtUtc, rotation.CancelledAtUtc);
        Assert.Equal("Relief delayed", rotation.CancellationNotes);
        Assert.Throws<InvalidCrewRotationTransitionException>(() => rotation.Update(null, "4-4", new DateOnly(2026, 4, 1)));
    }
}
