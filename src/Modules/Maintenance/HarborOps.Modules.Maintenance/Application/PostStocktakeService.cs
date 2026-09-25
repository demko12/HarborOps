using System.Data;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class PostStocktakeService(DbContext dbContext, TimeProvider timeProvider)
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<PostStocktakeResponse> PostAsync(
        Guid stocktakeId,
        PostStocktakeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PostStocktakeValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new PostStocktakeValidationException(errors);
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var stocktake = await dbContext.Set<Stocktake>()
            .SingleOrDefaultAsync(candidate => candidate.Id == stocktakeId, cancellationToken);

        if (stocktake is null)
        {
            throw new StocktakeNotFoundException(stocktakeId);
        }

        if (stocktake.Status == StocktakeStatus.Posted)
        {
            throw new StocktakeAlreadyPostedException(stocktakeId);
        }

        stocktake.EnsureDraft();

        if (stocktake.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new StocktakeConcurrencyException(stocktakeId);
        }

        var lines = await dbContext.Set<StocktakeLine>()
            .AsNoTracking()
            .Where(line => line.StocktakeId == stocktakeId)
            .ToListAsync(cancellationToken);

        var itemIds = lines.Select(line => line.InventoryItemId).ToList();

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var evaluation = await StocktakePostingEvaluator.EvaluateAsync(
            dbContext,
            stocktake,
            lines,
            inventoryItems,
            cancellationToken);

        if (!string.Equals(evaluation.PreviewToken, request.PreviewToken, StringComparison.OrdinalIgnoreCase))
        {
            throw new StocktakeStalePreviewException(stocktakeId);
        }

        if (evaluation.Blockers.Count > 0)
        {
            throw new StocktakePostingBlockedException(evaluation.Blockers);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var adjustments = new List<InventoryAdjustment>();

        foreach (var line in lines.Where(line => line.Variance != 0).OrderBy(line => line.InventoryItemId))
        {
            var inventoryItem = inventoryItems[line.InventoryItemId];
            var quantityBefore = inventoryItem.Quantity;
            var countedQuantity = line.CountedQuantity!.Value;

            inventoryItem.ApplyStocktakeCount(countedQuantity);

            adjustments.Add(InventoryAdjustment.CreateFromStocktake(
                inventoryItem.Id,
                stocktake.Id,
                quantityBefore,
                countedQuantity,
                line.VarianceReason!,
                actorUserId,
                utcNow));
        }

        stocktake.MarkPosted(lines, actorUserId, utcNow);

        await dbContext.Set<InventoryAdjustment>().AddRangeAsync(adjustments, cancellationToken);

        try
        {
            // One SaveChangesAsync: every stock adjustment and the posted status commit together.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new StocktakeAlreadyPostedException(stocktakeId);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new PostStocktakeResponse(
            stocktake.Id,
            stocktake.Status.ToString(),
            stocktake.ConcurrencyToken,
            actorUserId,
            utcNow,
            await StocktakeReader.ReadAdjustmentsAsync(dbContext, stocktake.Id, cancellationToken));
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        };
    }
}
