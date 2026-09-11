using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class CompletePortCallClearanceService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<PortCallClearanceStatusResponse> CompleteAsync(
        Guid voyageId,
        Guid stopId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var transaction = await PortCallClearanceGuard.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);

        try
        {
            var stop = await PortCallClearanceGuard.LoadStopAsync(
                dbContext,
                voyageId,
                stopId,
                cancellationToken);

            PortCallClearanceGuard.EnsureStopHasNotDeparted(stop);

            var clearance = await PortCallClearanceGuard.LoadClearanceAsync(
                dbContext,
                stopId,
                cancellationToken);

            var items = await PortCallClearanceGuard.LoadItemsAsync(
                dbContext,
                clearance.Id,
                cancellationToken);

            var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            var recorded = clearance.Complete(items, actorUserId, occurredAtUtc);

            await dbContext.Set<PortCallClearanceEvent>().AddAsync(recorded, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new PortCallClearanceStatusResponse(
                clearance.Id,
                stopId,
                clearance.Status,
                actorUserId,
                occurredAtUtc);
        }
        catch (DbUpdateException exception)
        {
            throw new PortCallClearanceConflictException(stopId, exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
