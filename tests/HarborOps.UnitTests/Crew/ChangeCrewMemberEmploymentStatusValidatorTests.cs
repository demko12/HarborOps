using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class ChangeCrewMemberEmploymentStatusValidatorTests
{
    [Fact]
    public void Validate_returns_no_errors_for_a_valid_request()
    {
        var request = new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave };

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_status()
    {
        var request = new ChangeCrewMemberEmploymentStatusRequest { Status = " " };

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);

        Assert.Contains(ChangeCrewMemberEmploymentStatusValidator.StatusField, errors);
    }

    [Fact]
    public void Validate_rejects_a_status_outside_the_supported_vocabulary()
    {
        var request = new ChangeCrewMemberEmploymentStatusRequest { Status = "Inactive" };

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);

        Assert.Contains(ChangeCrewMemberEmploymentStatusValidator.StatusField, errors);
    }

    [Theory]
    [InlineData(CrewEmploymentStatuses.Active)]
    [InlineData(CrewEmploymentStatuses.OnLeave)]
    [InlineData(CrewEmploymentStatuses.Suspended)]
    [InlineData(CrewEmploymentStatuses.Terminated)]
    public void Validate_accepts_every_supported_status(string status)
    {
        var request = new ChangeCrewMemberEmploymentStatusRequest { Status = status };

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_notes_exceeding_max_length()
    {
        var request = new ChangeCrewMemberEmploymentStatusRequest
        {
            Status = CrewEmploymentStatuses.OnLeave,
            Notes = new string('n', CrewEmploymentStatusHistory.NotesMaxLength + 1),
        };

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);

        Assert.Contains(ChangeCrewMemberEmploymentStatusValidator.NotesField, errors);
    }

    [Fact]
    public void Validate_allows_a_missing_notes_value()
    {
        var request = new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Active, Notes = null };

        var errors = ChangeCrewMemberEmploymentStatusValidator.Validate(request);

        Assert.Empty(errors);
    }
}
