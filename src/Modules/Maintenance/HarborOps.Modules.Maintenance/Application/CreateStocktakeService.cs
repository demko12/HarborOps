using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CreateStocktakeService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<StocktakeDetails> CreateAsync(
        CreateStocktakeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CreateStocktakeValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CreateStocktakeValidationException(errors);
        }

        var requestedItemIds = request.InventoryItemIds!.ToList();

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => requestedItemIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var missingItemIds = requestedItemIds
            .Where(itemId => inventoryItems.TrueForAll(item => item.Id != itemId))
            .ToList();

        if (missingItemIds.Count > 0)
        {
            throw new StocktakeItemsNotFoundException(missingItemIds);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        var stocktake = Stocktake.Create(notes, actorUserId, utcNow);

        var lines = inventoryItems
            .Select(item => StocktakeLine.Create(stocktake.Id, item, utcNow))
            .ToList();

        await dbContext.Set<Stocktake>().AddAsync(stocktake, cancellationToken);
        await dbContext.Set<StocktakeLine>().AddRangeAsync(lines, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StocktakeReader.ReadDetailsAsync(dbContext, stocktake.Id, cancellationToken))!;
    }
}
