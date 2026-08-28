using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class CrewAssignmentTests
{
    [Fact]
    public void Activate_sets_status_to_active()
    {
        var assignment = CreatePlanned();

        assignment.Activate();

        Assert.Equal(CrewAssignment.ActiveStatus, assignment.Status);
    }

    [Fact]
    public void Complete_sets_the_actual_sign_off_date_port_and_completed_status()
    {
        var assignment = CreatePlanned();
        var signOffDate = assignment.SignOnDate.AddDays(30);

        var signOffPortId = Guid.NewGuid();
        assignment.Complete(signOffDate, signOffPortId);

        Assert.Equal(signOffDate, assignment.ActualSignOffDate);
        Assert.Equal(signOffPortId, assignment.SignOffPortId);
        Assert.Equal(CrewAssignment.CompletedStatus, assignment.Status);
    }

    [Fact]
    public void Activate_rejects_a_terminal_or_already_active_assignment()
    {
        var assignment = CreatePlanned();
        assignment.Activate();

        Assert.Throws<InvalidCrewAssignmentTransitionException>(assignment.Activate);
    }

    [Fact]
    public void Cancel_retains_the_actor_timestamp_and_notes()
    {
        var assignment = CreatePlanned();
        var actorId = Guid.NewGuid();
        var cancelledAtUtc = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc);

        assignment.Cancel(actorId, cancelledAtUtc, "Operational change");

        Assert.Equal(CrewAssignment.CancelledStatus, assignment.Status);
        Assert.Equal(actorId, assignment.CancelledByUserId);
        Assert.Equal(cancelledAtUtc, assignment.CancelledAtUtc);
        Assert.Equal("Operational change", assignment.CancellationNotes);
        Assert.Throws<InvalidCrewAssignmentTransitionException>(() => assignment.Complete(new DateOnly(2026, 2, 1), null));
    }

    private static CrewAssignment CreatePlanned() =>
        CrewAssignment.CreatePlanned(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            signOnPortId: null,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 1));
}
