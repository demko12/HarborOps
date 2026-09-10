using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPreventiveWorkForecastValidatorTests
{
    private static readonly DateOnly From = new(2026, 9, 1);

    [Fact]
    public void Validate_accepts_a_bounded_window()
    {
        Assert.Empty(GetPreventiveWorkForecastValidator.Validate(CreateValidRequest()));
    }

    [Fact]
    public void Validate_requires_both_window_dates()
    {
        var errors = GetPreventiveWorkForecastValidator.Validate(new GetPreventiveWorkForecastRequest());

        Assert.Contains(GetPreventiveWorkForecastValidator.FromField, errors);
        Assert.Contains(GetPreventiveWorkForecastValidator.ToField, errors);
    }

    [Fact]
    public void Validate_rejects_a_window_that_ends_before_it_starts()
    {
        var request = CreateValidRequest() with { To = From.AddDays(-1) };

        var errors = GetPreventiveWorkForecastValidator.Validate(request);

        Assert.Contains(GetPreventiveWorkForecastValidator.ToField, errors);
    }

    [Fact]
    public void Validate_accepts_a_window_at_the_maximum_length()
    {
        var request = CreateValidRequest() with
        {
            To = From.AddDays(GetPreventiveWorkForecastValidator.MaxWindowDays),
        };

        Assert.Empty(GetPreventiveWorkForecastValidator.Validate(request));
    }

    [Fact]
    public void Validate_rejects_a_window_longer_than_the_maximum()
    {
        var request = CreateValidRequest() with
        {
            To = From.AddDays(GetPreventiveWorkForecastValidator.MaxWindowDays + 1),
        };

        var errors = GetPreventiveWorkForecastValidator.Validate(request);

        Assert.Contains(GetPreventiveWorkForecastValidator.ToField, errors);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("generated")]
    [InlineData("COMPLETED")]
    [InlineData("Cancelled")]
    public void Validate_accepts_known_states_case_insensitively(string state)
    {
        var request = CreateValidRequest() with { State = state };

        Assert.Empty(GetPreventiveWorkForecastValidator.Validate(request));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("7")]
    public void Validate_rejects_an_unknown_state(string state)
    {
        var request = CreateValidRequest() with { State = state };

        var errors = GetPreventiveWorkForecastValidator.Validate(request);

        Assert.Contains(GetPreventiveWorkForecastValidator.StateField, errors);
    }

    [Fact]
    public void Validate_rejects_a_page_number_below_one()
    {
        var request = CreateValidRequest() with { PageNumber = 0 };

        var errors = GetPreventiveWorkForecastValidator.Validate(request);

        Assert.Contains(GetPreventiveWorkForecastValidator.PageNumberField, errors);
    }

    [Fact]
    public void Validate_rejects_a_page_size_below_one()
    {
        var request = CreateValidRequest() with { PageSize = 0 };

        var errors = GetPreventiveWorkForecastValidator.Validate(request);

        Assert.Contains(GetPreventiveWorkForecastValidator.PageSizeField, errors);
    }

    [Fact]
    public void Validate_rejects_a_page_size_above_the_maximum()
    {
        var request = CreateValidRequest() with
        {
            PageSize = GetPreventiveWorkForecastValidator.MaxPageSize + 1,
        };

        var errors = GetPreventiveWorkForecastValidator.Validate(request);

        Assert.Contains(GetPreventiveWorkForecastValidator.PageSizeField, errors);
    }

    private static GetPreventiveWorkForecastRequest CreateValidRequest()
    {
        return new GetPreventiveWorkForecastRequest
        {
            From = From,
            To = From.AddDays(30),
        };
    }
}
