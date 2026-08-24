using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CompleteDryDockService(DbContext dbContext)
{
    public async Task<DryDockTransitionResponse> CompleteAsync(
        Guid dryDockId,
        CompleteDryDockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CompleteDryDockValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CompleteDryDockValidationException(errors);
        }

        var dryDock = await dbContext.Set<DryDock>()
            .FirstOrDefaultAsync(item => item.Id == dryDockId, cancellationToken);

        if (dryDock is null)
        {
            throw new DryDockNotFoundException(dryDockId);
        }

        dryDock.Complete(request.EndDate!.Value, request.ActualCost!.Value);

        await dbContext.SaveChangesAsync(cancellationToken);

        return DryDockTransitionResult.From(dryDock);
    }
}
