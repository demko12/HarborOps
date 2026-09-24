using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RefreshStocktakeLinesValidatorTests
{
    [Fact]
    public void Validate_accepts_a_token_and_distinct_items()
    {
        var errors = RefreshStocktakeLinesValidator.Validate(new RefreshStocktakeLinesRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            InventoryItemIds = [Guid.NewGuid()],
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_requires_the_concurrency_token_and_items()
    {
        var errors = RefreshStocktakeLinesValidator.Validate(new RefreshStocktakeLinesRequest());

        Assert.Contains(RefreshStocktakeLinesValidator.ExpectedConcurrencyTokenField, errors.Keys);
        Assert.Contains(RefreshStocktakeLinesValidator.InventoryItemIdsField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_duplicate_items()
    {
        var itemId = Guid.NewGuid();

        var errors = RefreshStocktakeLinesValidator.Validate(new RefreshStocktakeLinesRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            InventoryItemIds = [itemId, itemId],
        });

        Assert.Contains(RefreshStocktakeLinesValidator.InventoryItemIdsField, errors.Keys);
    }
}
