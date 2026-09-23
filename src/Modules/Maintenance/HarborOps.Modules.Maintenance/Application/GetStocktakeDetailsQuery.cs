using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetStocktakeDetailsQuery(DbContext dbContext)
{
    public async Task<StocktakeDetails> GetDetailsAsync(Guid stocktakeId, CancellationToken cancellationToken)
    {
        var details = await StocktakeReader.ReadDetailsAsync(dbContext, stocktakeId, cancellationToken);

        if (details is null)
        {
            throw new StocktakeNotFoundException(stocktakeId);
        }

        return details;
    }
}
