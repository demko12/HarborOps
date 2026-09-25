using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewQualificationEligibilityValidatorTests
{
    [Fact]
    public void Validate_accepts_an_inclusive_window_of_exactly_366_days()
    {
        var request = new GetCrewQualificationEligibilityRequest
        {
            CrewMemberId = Guid.NewGuid(),
            RankId = Guid.NewGuid(),
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 1).AddDays(365),
        };

        Assert.Empty(GetCrewQualificationEligibilityValidator.Validate(request));
    }

    [Fact]
    public void Validate_rejects_a_window_of_367_days()
    {
        var request = new GetCrewQualificationEligibilityRequest
        {
            CrewMemberId = Guid.NewGuid(),
            RankId = Guid.NewGuid(),
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 1).AddDays(366),
        };

        var errors = GetCrewQualificationEligibilityValidator.Validate(request);

        Assert.Contains(GetCrewQualificationEligibilityValidator.ToField, errors);
    }

    [Fact]
    public void Validate_requires_crew_member_and_rank_ids()
    {
        var errors = GetCrewQualificationEligibilityValidator.Validate(new GetCrewQualificationEligibilityRequest());

        Assert.Contains(GetCrewQualificationEligibilityValidator.CrewMemberIdField, errors);
        Assert.Contains(GetCrewQualificationEligibilityValidator.RankIdField, errors);
    }
}
