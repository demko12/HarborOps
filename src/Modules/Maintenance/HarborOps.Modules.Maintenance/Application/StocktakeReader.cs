using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class StocktakeReader
{
    internal static async Task<StocktakeDetails?> ReadDetailsAsync(
        DbContext dbContext,
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        var stocktake = await dbContext.Set<Stocktake>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == stocktakeId, cancellationToken);

        if (stocktake is null)
        {
            return null;
        }

        var lines = await (
            from line in dbContext.Set<StocktakeLine>().AsNoTracking()
            join item in dbContext.Set<InventoryItem>().AsNoTracking()
                on line.InventoryItemId equals item.Id
            join unit in dbContext.Set<UnitOfMeasure>().AsNoTracking()
                on item.UnitId equals unit.Id
            where line.StocktakeId == stocktakeId
            orderby item.Name, item.PartNumber, item.Id
            select new StocktakeLineItem(
                line.InventoryItemId,
                item.Name,
                item.PartNumber,
                unit.Abbreviation,
                line.BaselineQuantity,
                line.BaselineCapturedUtc,
                line.CountedQuantity,
                line.Variance,
                line.VarianceReason,
                line.CountedByUserId,
                line.CountedUtc))
            .ToListAsync(cancellationToken);

        var adjustments = await ReadAdjustmentsAsync(dbContext, stocktakeId, cancellationToken);

        return new StocktakeDetails(
            stocktake.Id,
            stocktake.Status.ToString(),
            stocktake.Notes,
            stocktake.ConcurrencyToken,
            stocktake.CreatedByUserId,
            stocktake.CreatedUtc,
            stocktake.LastModifiedByUserId,
            stocktake.LastModifiedUtc,
            stocktake.PostedByUserId,
            stocktake.PostedUtc,
            stocktake.CancelledByUserId,
            stocktake.CancelledUtc,
            stocktake.CancellationReason,
            lines,
            adjustments);
    }

    internal static async Task<IReadOnlyList<InventoryAdjustmentItem>> ReadAdjustmentsAsync(
        DbContext dbContext,
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .Where(adjustment => adjustment.StocktakeId == stocktakeId)
            .OrderBy(adjustment => adjustment.InventoryItemId)
            .Select(adjustment => new InventoryAdjustmentItem(
                adjustment.Id,
                adjustment.InventoryItemId,
                adjustment.StocktakeId,
                adjustment.QuantityBefore,
                adjustment.CountedQuantity,
                adjustment.QuantityDelta,
                adjustment.Reason,
                adjustment.ActorUserId,
                adjustment.AdjustedUtc))
            .ToListAsync(cancellationToken);
    }
}
