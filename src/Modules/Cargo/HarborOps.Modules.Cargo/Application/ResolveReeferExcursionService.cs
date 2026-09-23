using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class ResolveReeferExcursionService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ReeferExcursionListItem> ResolveAsync(
        Guid containerId,
        Guid excursionId,
        Guid actorUserId,
        ResolveReeferExcursionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var errors = ReeferMonitoringValidator.ValidateResolution(request);
        if (errors.Count > 0)
        {
            throw new ReeferMonitoringValidationException(errors);
        }

        var excursion = await ReeferMonitoringGuard.FindExcursionAsync(
            dbContext,
            containerId,
            excursionId,
            cancellationToken);

        if (!excursion.IsOpen)
        {
            throw new ReeferExcursionTransitionException(
                excursion.Id,
                excursion.Status,
                ReeferExcursionNotOpenException.ResolveAction);
        }

        ReeferMonitoringValidator.TryParseDisposition(request.Disposition, out var disposition);

        excursion.Resolve(
            disposition,
            request.Explanation!,
            actorUserId,
            timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return ReeferMonitoringMapper.ToListItem(excursion);
    }
}
