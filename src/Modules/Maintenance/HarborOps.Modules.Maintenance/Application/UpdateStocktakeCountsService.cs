using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class UpdateStocktakeCountsService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<StocktakeDetails> UpdateCountsAsync(
        Guid stocktakeId,
        UpdateStocktakeCountsRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateStocktakeCountsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateStocktakeCountsValidationException(errors);
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

        var lines = await dbContext.Set<StocktakeLine>()
            .Where(line => line.StocktakeId == stocktakeId)
            .ToListAsync(cancellationToken);

        var lineErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        for (var index = 0; index < request.Lines!.Count; index++)
        {
            var entry = request.Lines[index];
            var line = lines.SingleOrDefault(candidate => candidate.InventoryItemId == entry.InventoryItemId);

            if (line is null)
            {
                lineErrors[$"{UpdateStocktakeCountsValidator.LinesField}[{index}].inventoryItemId"] =
                    ["Inventory item is not part of this stocktake."];
            }
            else if (entry.CountedQuantity is { } counted
                && counted != line.BaselineQuantity
                && string.IsNullOrWhiteSpace(entry.Reason))
            {
                lineErrors[$"{UpdateStocktakeCountsValidator.LinesField}[{index}].reason"] =
                    ["A reason is required when the count differs from the baseline."];
            }
        }

        if (lineErrors.Count > 0)
        {
            throw new UpdateStocktakeCountsValidationException(lineErrors);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in request.Lines)
        {
            lines
                .Single(line => line.InventoryItemId == entry.InventoryItemId)
                .RecordCount(entry.CountedQuantity, entry.Reason, actorUserId, utcNow);
        }

        stocktake.RecordChange(actorUserId, utcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StocktakeReader.ReadDetailsAsync(dbContext, stocktakeId, cancellationToken))!;
    }
}
