using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class UpdateStocktakeCountsValidatorTests
{
    [Fact]
    public void Validate_requires_the_concurrency_token_and_lines()
    {
        var errors = UpdateStocktakeCountsValidator.Validate(new UpdateStocktakeCountsRequest());

        Assert.Contains(UpdateStocktakeCountsValidator.ExpectedConcurrencyTokenField, errors.Keys);
        Assert.Contains(UpdateStocktakeCountsValidator.LinesField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_negative_and_imprecise_quantities()
    {
        var errors = UpdateStocktakeCountsValidator.Validate(new UpdateStocktakeCountsRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines =
            [
                new StocktakeCountEntry { InventoryItemId = Guid.NewGuid(), CountedQuantity = -1m },
                new StocktakeCountEntry { InventoryItemId = Guid.NewGuid(), CountedQuantity = 1.2345m },
            ],
        });

        Assert.Contains("lines[0].countedQuantity", errors.Keys);
        Assert.Contains("lines[1].countedQuantity", errors.Keys);
    }

    [Fact]
    public void Validate_accepts_zero_and_three_decimal_places()
    {
        var errors = UpdateStocktakeCountsValidator.Validate(new UpdateStocktakeCountsRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines =
            [
                new StocktakeCountEntry { InventoryItemId = Guid.NewGuid(), CountedQuantity = 0m },
                new StocktakeCountEntry { InventoryItemId = Guid.NewGuid(), CountedQuantity = 1.125m },
            ],
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_duplicate_and_missing_item_ids()
    {
        var itemId = Guid.NewGuid();

        var errors = UpdateStocktakeCountsValidator.Validate(new UpdateStocktakeCountsRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines =
            [
                new StocktakeCountEntry { InventoryItemId = itemId, CountedQuantity = 1m },
                new StocktakeCountEntry { InventoryItemId = itemId, CountedQuantity = 2m },
                new StocktakeCountEntry { CountedQuantity = 3m },
            ],
        });

        Assert.Contains("lines[1].inventoryItemId", errors.Keys);
        Assert.Contains("lines[2].inventoryItemId", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_a_reason_that_is_too_long()
    {
        var errors = UpdateStocktakeCountsValidator.Validate(new UpdateStocktakeCountsRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines =
            [
                new StocktakeCountEntry { InventoryItemId = Guid.NewGuid(), CountedQuantity = 1m, Reason = new string('r', 501) },
            ],
        });

        Assert.Contains("lines[0].reason", errors.Keys);
    }
}
