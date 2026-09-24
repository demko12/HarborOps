using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CreateStocktakeValidator
{
    internal const string InventoryItemIdsField = "inventoryItemIds";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> Validate(CreateStocktakeRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.InventoryItemIds is null || request.InventoryItemIds.Count == 0)
        {
            errors[InventoryItemIdsField] = ["At least one inventory item is required."];
        }
        else if (request.InventoryItemIds.Count > Stocktake.MaxLines)
        {
            errors[InventoryItemIdsField] = [$"At most {Stocktake.MaxLines} inventory items are allowed."];
        }
        else if (request.InventoryItemIds.Any(itemId => itemId == Guid.Empty))
        {
            errors[InventoryItemIdsField] = ["Inventory item ids must not be empty."];
        }
        else if (request.InventoryItemIds.Distinct().Count() != request.InventoryItemIds.Count)
        {
            errors[InventoryItemIdsField] = ["Inventory item ids must be distinct."];
        }

        if (request.Notes is { Length: > Stocktake.NotesMaxLength })
        {
            errors[NotesField] = [$"Notes must not exceed {Stocktake.NotesMaxLength} characters."];
        }

        return errors;
    }
}
