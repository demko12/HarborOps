using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class VoidPortCallActivityService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<PortCallActivityChangeResponse> VoidAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        VoidPortCallActivityRequest request,
        Guid actorUserId,
        string actorName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        var errors = PortCallLogValidator.ValidateVoid(request);
        if (errors.Count > 0)
        {
            throw new PortCallLogValidationException(errors);
        }

        var transaction = await PortCallClearanceGuard.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);

        try
        {
            var activity = await PortCallActivityChange.LoadForChangeAsync(
                dbContext,
                voyageId,
                stopId,
                activityId,
                request.Version!.Value,
                cancellationToken);

            var voidedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            var revision = activity.Void(request.Reason!.Trim(), actorUserId, actorName, voidedAtUtc);

            await dbContext.Set<PortCallActivityRevision>().AddAsync(revision, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new PortCallActivityChangeResponse(
                activity.Id,
                activity.Status,
                activity.Revision,
                activity.ConcurrencyToken,
                actorUserId,
                voidedAtUtc);
        }
        catch (DbUpdateException exception)
        {
            throw new PortCallLogConflictException(stopId, exception);
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
