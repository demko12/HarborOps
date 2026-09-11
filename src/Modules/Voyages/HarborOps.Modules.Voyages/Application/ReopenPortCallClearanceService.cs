using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class ReopenPortCallClearanceService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<PortCallClearanceStatusResponse> ReopenAsync(
        Guid voyageId,
        Guid stopId,
        ReopenPortCallClearanceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortCallClearanceValidator.ValidateReopen(request);
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

            var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            var recorded = clearance.Reopen(request.Reason!.Trim(), actorUserId, occurredAtUtc);

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
