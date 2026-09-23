using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RecordPortCallActivityService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<PortCallActivityChangeResponse> RecordAsync(
        Guid voyageId,
        Guid stopId,
        RecordPortCallActivityRequest request,
        Guid actorUserId,
        string actorName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        var utcNow = timeProvider.GetUtcNow();

        var errors = PortCallLogValidator.ValidateRecord(request, utcNow);
        if (errors.Count > 0)
        {
            throw new PortCallLogValidationException(errors);
        }

        var transaction = await PortCallClearanceGuard.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);

        try
        {
            var (_, stop) = await PortCallLog.LoadStopAsync(
                dbContext,
                voyageId,
                stopId,
                tracking: false,
                cancellationToken);

            if (stop.Ata is null)
            {
                throw new PortCallActivityStopNotArrivedException(stop.Id, stop.Status);
            }

            var statement = await PortCallLog.FindStatementAsync(
                dbContext,
                stopId,
                tracking: false,
                cancellationToken);

            statement?.EnsureDraft();

            var sequences = await dbContext.Set<PortCallActivity>()
                .AsNoTracking()
                .Where(activity => activity.VoyageStopId == stopId)
                .Select(activity => activity.Sequence)
                .ToListAsync(cancellationToken);

            if (sequences.Count >= PortCallActivity.MaxActivitiesPerStop)
            {
                throw new PortCallActivityLimitReachedException(stopId, PortCallActivity.MaxActivitiesPerStop);
            }

            var recordedAtUtc = utcNow.UtcDateTime;

            var (activity, revision) = PortCallActivity.Record(
                voyageId,
                stopId,
                sequences.Count == 0 ? 1 : sequences.Max() + 1,
                request.ActivityType!.Trim(),
                request.OccurredAt!.Value.UtcDateTime,
                request.EndedAt?.UtcDateTime,
                PortCallLogValidator.Normalize(request.Notes),
                actorUserId,
                actorName,
                recordedAtUtc);

            await dbContext.Set<PortCallActivity>().AddAsync(activity, cancellationToken);
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
                recordedAtUtc);
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
