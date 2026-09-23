using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RefreshStocktakeLinesValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string InventoryItemIdsField = "inventoryItemIds";

    internal static IReadOnlyDictionary<string, string[]> Validate(RefreshStocktakeLinesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

        if (request.InventoryItemIds is null || request.InventoryItemIds.Count == 0)
        {
            errors[InventoryItemIdsField] = ["At least one inventory item is required."];
        }
        else if (request.InventoryItemIds.Count > Stocktake.MaxLines)
        {
            errors[InventoryItemIdsField] = [$"At most {Stocktake.MaxLines} inventory items are allowed."];
        }
        else if (request.InventoryItemIds.Distinct().Count() != request.InventoryItemIds.Count)
        {
            errors[InventoryItemIdsField] = ["Inventory item ids must be distinct."];
        }

        return errors;
    }
}
