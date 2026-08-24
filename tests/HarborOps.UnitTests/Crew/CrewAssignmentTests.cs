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
    public void SignOff_sets_the_actual_sign_off_date()
    {
        var assignment = CreatePlanned();
        var signOffDate = assignment.SignOnDate.AddDays(30);

        assignment.SignOff(signOffDate);

        Assert.Equal(signOffDate, assignment.ActualSignOffDate);
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
