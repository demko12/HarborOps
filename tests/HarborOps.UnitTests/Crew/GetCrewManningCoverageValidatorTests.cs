using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewManningCoverageValidatorTests
{
    [Fact]
    public void Validate_accepts_an_inclusive_window_of_exactly_90_days()
    {
        var request = new GetCrewManningCoverageRequest
        {
            VesselId = Guid.NewGuid(),
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 1).AddDays(89), // 90 days inclusive
        };

        var errors = GetCrewManningCoverageValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_window_of_91_days()
    {
        var request = new GetCrewManningCoverageRequest
        {
            VesselId = Guid.NewGuid(),
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 1).AddDays(90), // 91 days inclusive
        };

        var errors = GetCrewManningCoverageValidator.Validate(request);

        Assert.Contains(GetCrewManningCoverageValidator.ToField, errors);
    }

    [Fact]
    public void Validate_rejects_a_to_date_before_the_from_date()
    {
        var request = new GetCrewManningCoverageRequest
        {
            VesselId = Guid.NewGuid(),
            From = new DateOnly(2026, 1, 10),
            To = new DateOnly(2026, 1, 1),
        };

        var errors = GetCrewManningCoverageValidator.Validate(request);

        Assert.Contains(GetCrewManningCoverageValidator.ToField, errors);
    }

    [Fact]
    public void Validate_requires_a_vessel_id()
    {
        var request = new GetCrewManningCoverageRequest
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 2),
        };

        var errors = GetCrewManningCoverageValidator.Validate(request);

        Assert.Contains(GetCrewManningCoverageValidator.VesselIdField, errors);
    }
}
