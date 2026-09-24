using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetStocktakePostingPreviewQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<StocktakePostingPreview> GetPreviewAsync(
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        var stocktake = await dbContext.Set<Stocktake>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == stocktakeId, cancellationToken);

        if (stocktake is null)
        {
            throw new StocktakeNotFoundException(stocktakeId);
        }

        stocktake.EnsureDraft();

        var lines = await dbContext.Set<StocktakeLine>()
            .AsNoTracking()
            .Where(line => line.StocktakeId == stocktakeId)
            .ToListAsync(cancellationToken);

        var itemIds = lines.Select(line => line.InventoryItemId).ToList();

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var evaluation = await StocktakePostingEvaluator.EvaluateAsync(
            dbContext,
            stocktake,
            lines,
            inventoryItems,
            cancellationToken);

        return new StocktakePostingPreview(
            stocktake.Id,
            stocktake.ConcurrencyToken,
            evaluation.PreviewToken,
            evaluation.Blockers.Count == 0,
            evaluation.Lines,
            evaluation.Blockers,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
