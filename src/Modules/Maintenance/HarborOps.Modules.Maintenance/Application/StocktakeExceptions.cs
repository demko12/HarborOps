using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class CreateStocktakeValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create stocktake validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetStocktakesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get stocktakes validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class UpdateStocktakeCountsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update stocktake counts validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RefreshStocktakeLinesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Refresh stocktake lines validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CancelStocktakeValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cancel stocktake validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PostStocktakeValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Post stocktake validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class StocktakeNotFoundException(Guid stocktakeId)
    : Exception($"Stocktake '{stocktakeId}' was not found.")
{
    internal Guid StocktakeId { get; } = stocktakeId;
}

internal sealed class StocktakeItemsNotFoundException(IReadOnlyList<Guid> inventoryItemIds)
    : Exception($"Inventory items {string.Join(", ", inventoryItemIds)} were not found in the maintenance module.")
{
    internal IReadOnlyList<Guid> InventoryItemIds { get; } = inventoryItemIds;
}

internal sealed class StocktakeConcurrencyException(Guid stocktakeId)
    : Exception($"Stocktake '{stocktakeId}' was changed by another request. Reload it and try again.")
{
    internal Guid StocktakeId { get; } = stocktakeId;
}

internal sealed class StocktakeAlreadyPostedException(Guid stocktakeId)
    : Exception($"Stocktake '{stocktakeId}' has already been posted.")
{
    internal Guid StocktakeId { get; } = stocktakeId;
}

internal sealed class StocktakeStalePreviewException(Guid stocktakeId)
    : Exception($"Inventory for stocktake '{stocktakeId}' changed after the posting preview. Review a new preview before posting.")
{
    internal Guid StocktakeId { get; } = stocktakeId;
}

internal sealed class StocktakePostingBlockedException(IReadOnlyList<StocktakePostingBlocker> blockers)
    : Exception(string.Join(" ", blockers.Select(blocker => blocker.Message)))
{
    internal IReadOnlyList<StocktakePostingBlocker> Blockers { get; } = blockers;
}
