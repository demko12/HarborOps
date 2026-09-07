using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class RegisterCrewPositionValidatorTests
{
    [Fact]
    public void Validate_returns_no_errors_for_a_valid_request()
    {
        var request = CreateValidRequest();

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_rank_name()
    {
        var request = CreateValidRequest() with { RankName = " " };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.RankNameField, errors);
    }

    [Fact]
    public void Validate_rejects_a_rank_name_exceeding_max_length()
    {
        var request = CreateValidRequest() with { RankName = new string('a', CrewPosition.RankNameMaxLength + 1) };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.RankNameField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_department()
    {
        var request = CreateValidRequest() with { Department = null };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.DepartmentField, errors);
    }

    [Fact]
    public void Validate_rejects_an_unsupported_department()
    {
        var request = CreateValidRequest() with { Department = "Catering & Housekeeping" };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.DepartmentField, errors);
    }

    [Theory]
    [InlineData(CrewPositionDepartments.Deck)]
    [InlineData(CrewPositionDepartments.Engine)]
    [InlineData(CrewPositionDepartments.Catering)]
    [InlineData(CrewPositionDepartments.Electrical)]
    public void Validate_accepts_every_supported_department(string department)
    {
        var request = CreateValidRequest() with { Department = department };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.DoesNotContain(CrewPositionFieldValidation.DepartmentField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_seniority_level()
    {
        var request = CreateValidRequest() with { SeniorityLevel = null };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.SeniorityLevelField, errors);
    }

    [Fact]
    public void Validate_rejects_an_unsupported_seniority_level()
    {
        var request = CreateValidRequest() with { SeniorityLevel = "Master" };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.SeniorityLevelField, errors);
    }

    [Theory]
    [InlineData(CrewPositionSeniorityLevels.Officer)]
    [InlineData(CrewPositionSeniorityLevels.SeniorRating)]
    [InlineData(CrewPositionSeniorityLevels.JuniorRating)]
    [InlineData(CrewPositionSeniorityLevels.Cadet)]
    public void Validate_accepts_every_supported_seniority_level(string seniorityLevel)
    {
        var request = CreateValidRequest() with { SeniorityLevel = seniorityLevel };

        var errors = RegisterCrewPositionValidator.Validate(request);

        Assert.DoesNotContain(CrewPositionFieldValidation.SeniorityLevelField, errors);
    }

    private static RegisterCrewPositionRequest CreateValidRequest()
    {
        return new RegisterCrewPositionRequest
        {
            RankName = "Bosun",
            Department = CrewPositionDepartments.Deck,
            SeniorityLevel = CrewPositionSeniorityLevels.SeniorRating,
        };
    }
}
