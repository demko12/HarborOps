using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CancelDryDockService(DbContext dbContext)
{
    public async Task<DryDockTransitionResponse> CancelAsync(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        var dryDock = await dbContext.Set<DryDock>()
            .FirstOrDefaultAsync(item => item.Id == dryDockId, cancellationToken);

        if (dryDock is null)
        {
            throw new DryDockNotFoundException(dryDockId);
        }

        dryDock.Cancel();

        await dbContext.SaveChangesAsync(cancellationToken);

        return DryDockTransitionResult.From(dryDock);
    }
}
