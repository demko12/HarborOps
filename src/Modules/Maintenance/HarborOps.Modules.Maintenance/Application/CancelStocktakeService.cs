using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CancelStocktakeService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<StocktakeDetails> CancelAsync(
        Guid stocktakeId,
        CancelStocktakeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CancelStocktakeValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CancelStocktakeValidationException(errors);
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

        stocktake.Cancel(request.Reason!, actorUserId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StocktakeReader.ReadDetailsAsync(dbContext, stocktakeId, cancellationToken))!;
    }
}
