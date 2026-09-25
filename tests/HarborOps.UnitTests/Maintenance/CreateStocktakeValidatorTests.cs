using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CreateStocktakeValidatorTests
{
    [Fact]
    public void Validate_accepts_distinct_items()
    {
        var errors = CreateStocktakeValidator.Validate(new CreateStocktakeRequest
        {
            InventoryItemIds = [Guid.NewGuid(), Guid.NewGuid()],
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_missing_items()
    {
        var errors = CreateStocktakeValidator.Validate(new CreateStocktakeRequest());

        Assert.Contains(CreateStocktakeValidator.InventoryItemIdsField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_duplicate_items()
    {
        var itemId = Guid.NewGuid();

        var errors = CreateStocktakeValidator.Validate(new CreateStocktakeRequest
        {
            InventoryItemIds = [itemId, itemId],
        });

        Assert.Contains(CreateStocktakeValidator.InventoryItemIdsField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_empty_item_ids()
    {
        var errors = CreateStocktakeValidator.Validate(new CreateStocktakeRequest
        {
            InventoryItemIds = [Guid.Empty],
        });

        Assert.Contains(CreateStocktakeValidator.InventoryItemIdsField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_more_than_two_hundred_items()
    {
        var errors = CreateStocktakeValidator.Validate(new CreateStocktakeRequest
        {
            InventoryItemIds = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList(),
        });

        Assert.Contains(CreateStocktakeValidator.InventoryItemIdsField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_notes_that_are_too_long()
    {
        var errors = CreateStocktakeValidator.Validate(new CreateStocktakeRequest
        {
            InventoryItemIds = [Guid.NewGuid()],
            Notes = new string('n', 1001),
        });

        Assert.Contains(CreateStocktakeValidator.NotesField, errors.Keys);
    }
}
