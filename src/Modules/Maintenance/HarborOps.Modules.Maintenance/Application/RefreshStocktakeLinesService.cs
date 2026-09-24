using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class RefreshStocktakeLinesService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<StocktakeDetails> RefreshAsync(
        Guid stocktakeId,
        RefreshStocktakeLinesRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RefreshStocktakeLinesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RefreshStocktakeLinesValidationException(errors);
        }

        var stocktake = await dbContext.Set<Stocktake>()
            .SingleOrDefaultAsync(candidate => candidate.Id == stocktakeId, cancellationToken);

        if (stocktake is null)
        {
            throw new StocktakeNotFoundException(stocktakeId);
        }

        stocktake.EnsureDraft();

        if (stocktake.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new StocktakeConcurrencyException(stocktakeId);
        }

        var requestedItemIds = request.InventoryItemIds!.ToList();

        var lines = await dbContext.Set<StocktakeLine>()
            .Where(line => line.StocktakeId == stocktakeId && requestedItemIds.Contains(line.InventoryItemId))
            .ToListAsync(cancellationToken);

        var unknownItemIds = requestedItemIds
            .Where(itemId => lines.TrueForAll(line => line.InventoryItemId != itemId))
            .ToList();

        if (unknownItemIds.Count > 0)
        {
            throw new RefreshStocktakeLinesValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [RefreshStocktakeLinesValidator.InventoryItemIdsField] =
                    [$"Inventory items {string.Join(", ", unknownItemIds)} are not part of this stocktake."],
            });
        }

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => requestedItemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var line in lines)
        {
            line.RefreshBaseline(inventoryItems[line.InventoryItemId], utcNow);
        }

        stocktake.RecordChange(actorUserId, utcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StocktakeReader.ReadDetailsAsync(dbContext, stocktakeId, cancellationToken))!;
    }
}
