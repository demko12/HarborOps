using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class WaivePortCallClearanceItemService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<PortCallClearanceItemDecisionResponse> WaiveAsync(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        WaivePortCallClearanceItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortCallClearanceValidator.ValidateWaive(request);
        if (errors.Count > 0)
        {
            throw new PortCallClearanceValidationException(errors);
        }

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

            var item = await PortCallClearanceGuard.LoadItemAsync(
                dbContext,
                clearance.Id,
                itemId,
                cancellationToken);

            var decidedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            var recorded = clearance.WaiveItem(
                item,
                request.Reason!.Trim(),
                actorUserId,
                decidedAtUtc);

            await dbContext.Set<PortCallClearanceEvent>().AddAsync(recorded, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new PortCallClearanceItemDecisionResponse(
                item.Id,
                item.Status,
                actorUserId,
                decidedAtUtc);
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
