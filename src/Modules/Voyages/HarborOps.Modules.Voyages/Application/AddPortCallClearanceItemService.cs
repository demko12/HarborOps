using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class AddPortCallClearanceItemService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<AddPortCallClearanceItemResponse> AddAsync(
        Guid voyageId,
        Guid stopId,
        AddPortCallClearanceItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortCallClearanceValidator.ValidateAdd(request);
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

            var (item, recorded) = clearance.AddItem(
                request.Category!.Trim(),
                request.Title!.Trim(),
                request.IsRequired,
                request.DueAtUtc,
                PortCallClearanceValidator.Normalize(request.AuthorityReference),
                request.EvidenceDocumentId,
                actorUserId,
                occurredAtUtc);

            await dbContext.Set<PortCallClearanceItem>().AddAsync(item, cancellationToken);
            await dbContext.Set<PortCallClearanceEvent>().AddAsync(recorded, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new AddPortCallClearanceItemResponse(
                item.Id,
                clearance.Id,
                item.Status,
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
