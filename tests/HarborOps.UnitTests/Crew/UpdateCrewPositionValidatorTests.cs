using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class UpdateCrewPositionValidatorTests
{
    [Fact]
    public void Validate_returns_no_errors_for_a_valid_request()
    {
        var request = CreateValidRequest();

        var errors = UpdateCrewPositionValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_rank_name()
    {
        var request = CreateValidRequest() with { RankName = " " };

        var errors = UpdateCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.RankNameField, errors);
    }

    [Fact]
    public void Validate_rejects_a_rank_name_exceeding_max_length()
    {
        var request = CreateValidRequest() with { RankName = new string('a', CrewPosition.RankNameMaxLength + 1) };

        var errors = UpdateCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.RankNameField, errors);
    }

    [Fact]
    public void Validate_rejects_an_unsupported_department()
    {
        var request = CreateValidRequest() with { Department = "Bridge" };

        var errors = UpdateCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.DepartmentField, errors);
    }

    [Fact]
    public void Validate_rejects_an_unsupported_seniority_level()
    {
        var request = CreateValidRequest() with { SeniorityLevel = "Trainee" };

        var errors = UpdateCrewPositionValidator.Validate(request);

        Assert.Contains(CrewPositionFieldValidation.SeniorityLevelField, errors);
    }

    private static UpdateCrewPositionRequest CreateValidRequest()
    {
        return new UpdateCrewPositionRequest
        {
            RankName = "Bosun",
            Department = CrewPositionDepartments.Deck,
            SeniorityLevel = CrewPositionSeniorityLevels.SeniorRating,
        };
    }
}
