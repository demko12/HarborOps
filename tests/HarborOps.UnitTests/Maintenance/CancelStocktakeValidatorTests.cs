using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CancelStocktakeValidatorTests
{
    [Fact]
    public void Validate_accepts_a_token_and_reason()
    {
        var errors = CancelStocktakeValidator.Validate(new CancelStocktakeRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Reason = "Counted the wrong locker",
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Validate_requires_a_reason(string? reason)
    {
        var errors = CancelStocktakeValidator.Validate(new CancelStocktakeRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Reason = reason,
        });

        Assert.Contains(CancelStocktakeValidator.ReasonField, errors.Keys);
    }

    [Fact]
    public void Validate_requires_the_concurrency_token()
    {
        var errors = CancelStocktakeValidator.Validate(new CancelStocktakeRequest { Reason = "Duplicate" });

        Assert.Contains(CancelStocktakeValidator.ExpectedConcurrencyTokenField, errors.Keys);
    }
}
