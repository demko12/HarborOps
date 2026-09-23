using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetStocktakesValidatorTests
{
    [Fact]
    public void Validate_accepts_an_empty_request()
    {
        var errors = GetStocktakesValidator.Validate(new GetStocktakesRequest());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("Posted")]
    [InlineData("CANCELLED")]
    public void Validate_accepts_known_statuses_in_any_case(string status)
    {
        var errors = GetStocktakesValidator.Validate(new GetStocktakesRequest { Status = status });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_unknown_status_and_paging_out_of_range()
    {
        var errors = GetStocktakesValidator.Validate(new GetStocktakesRequest
        {
            Status = "Archived",
            PageNumber = 0,
            PageSize = 101,
        });

        Assert.Contains(GetStocktakesValidator.StatusField, errors.Keys);
        Assert.Contains(GetStocktakesValidator.PageNumberField, errors.Keys);
        Assert.Contains(GetStocktakesValidator.PageSizeField, errors.Keys);
    }
}
