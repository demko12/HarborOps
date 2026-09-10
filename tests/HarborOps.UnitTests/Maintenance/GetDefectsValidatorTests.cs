using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetDefectsValidatorTests
{
    [Fact]
    public void Validate_accepts_a_valid_request()
    {
        var errors = GetDefectsValidator.Validate(CreateValidRequest());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_null_status_and_severity_as_optional_filters()
    {
        var request = CreateValidRequest() with { Status = null, Severity = null };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_an_invalid_status()
    {
        var request = CreateValidRequest() with { Status = "Unknown" };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Contains(GetDefectsValidator.StatusField, errors);
    }

    [Fact]
    public void Validate_rejects_an_invalid_severity()
    {
        var request = CreateValidRequest() with { Severity = "Severe" };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Contains(GetDefectsValidator.SeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_a_page_number_below_one()
    {
        var request = CreateValidRequest() with { PageNumber = 0 };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Contains(GetDefectsValidator.PageNumberField, errors);
    }

    [Fact]
    public void Validate_rejects_a_page_size_below_one()
    {
        var request = CreateValidRequest() with { PageSize = 0 };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Contains(GetDefectsValidator.PageSizeField, errors);
    }

    [Fact]
    public void Validate_rejects_a_page_size_above_the_maximum()
    {
        var request = CreateValidRequest() with { PageSize = GetDefectsValidator.MaxPageSize + 1 };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Contains(GetDefectsValidator.PageSizeField, errors);
    }

    [Fact]
    public void Validate_accepts_a_page_size_at_exactly_the_maximum()
    {
        var request = CreateValidRequest() with { PageSize = GetDefectsValidator.MaxPageSize };

        var errors = GetDefectsValidator.Validate(request);

        Assert.Empty(errors);
    }

    private static GetDefectsRequest CreateValidRequest()
    {
        return new GetDefectsRequest
        {
            Status = "Reported",
            Severity = "Medium",
            VesselId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 25,
        };
    }
}
