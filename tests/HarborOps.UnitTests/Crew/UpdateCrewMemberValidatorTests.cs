using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class UpdateCrewMemberValidatorTests
{
    [Fact]
    public void Validate_returns_no_errors_for_a_valid_request()
    {
        var request = CreateValidRequest();

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_base_rank_id()
    {
        var request = CreateValidRequest() with { BaseRankId = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.BaseRankIdField, errors);
    }

    [Fact]
    public void Validate_rejects_an_empty_base_rank_id()
    {
        var request = CreateValidRequest() with { BaseRankId = Guid.Empty };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.BaseRankIdField, errors);
    }

    [Fact]
    public void Validate_rejects_an_empty_first_name()
    {
        var request = CreateValidRequest() with { FirstName = " " };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.FirstNameField, errors);
    }

    [Fact]
    public void Validate_rejects_a_first_name_exceeding_max_length()
    {
        var request = CreateValidRequest() with { FirstName = new string('a', CrewMember.FirstNameMaxLength + 1) };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.FirstNameField, errors);
    }

    [Fact]
    public void Validate_rejects_an_empty_last_name()
    {
        var request = CreateValidRequest() with { LastName = " " };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.LastNameField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_date_of_birth()
    {
        var request = CreateValidRequest() with { DateOfBirth = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.DateOfBirthField, errors);
    }

    [Fact]
    public void Validate_rejects_a_future_date_of_birth()
    {
        var request = CreateValidRequest() with { DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.DateOfBirthField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_nationality()
    {
        var request = CreateValidRequest() with { Nationality = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.NationalityField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_passport_number()
    {
        var request = CreateValidRequest() with { PassportNumber = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.PassportNumberField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_passport_expiry()
    {
        var request = CreateValidRequest() with { PassportExpiry = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.PassportExpiryField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_cdc_number()
    {
        var request = CreateValidRequest() with { CdcNumber = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.CdcNumberField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_hire_date()
    {
        var request = CreateValidRequest() with { HireDate = null };

        var errors = UpdateCrewMemberValidator.Validate(request);

        Assert.Contains(UpdateCrewMemberValidator.HireDateField, errors);
    }

    private static UpdateCrewMemberRequest CreateValidRequest()
    {
        return new UpdateCrewMemberRequest
        {
            FirstName = "Ivan",
            LastName = "Petrović",
            DateOfBirth = new DateOnly(1990, 5, 14),
            Nationality = "Montenegro",
            PassportNumber = "P1234567",
            PassportExpiry = new DateOnly(2028, 1, 1),
            CdcNumber = "CDC987654",
            HireDate = new DateOnly(2024, 1, 15),
            BaseRankId = Guid.NewGuid(),
        };
    }
}
