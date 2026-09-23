using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class CorrectPortCallActivityService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<PortCallActivityChangeResponse> CorrectAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        CorrectPortCallActivityRequest request,
        Guid actorUserId,
        string actorName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        var utcNow = timeProvider.GetUtcNow();

        var errors = PortCallLogValidator.ValidateCorrect(request, utcNow);
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

            var correctedAtUtc = utcNow.UtcDateTime;

            var revision = activity.Correct(
                request.ActivityType!.Trim(),
                request.OccurredAt!.Value.UtcDateTime,
                request.EndedAt?.UtcDateTime,
                PortCallLogValidator.Normalize(request.Notes),
                request.Reason!.Trim(),
                actorUserId,
                actorName,
                correctedAtUtc);

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
                correctedAtUtc);
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
