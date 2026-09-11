using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class UpdatePortCallClearanceItemService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task UpdateAsync(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        UpdatePortCallClearanceItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortCallClearanceValidator.ValidateUpdate(request);
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

            var recorded = clearance.EditItem(
                item,
                request.Category!.Trim(),
                request.Title!.Trim(),
                request.IsRequired,
                request.DueAtUtc,
                PortCallClearanceValidator.Normalize(request.AuthorityReference),
                request.EvidenceDocumentId,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime);

            await dbContext.Set<PortCallClearanceEvent>().AddAsync(recorded, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
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
