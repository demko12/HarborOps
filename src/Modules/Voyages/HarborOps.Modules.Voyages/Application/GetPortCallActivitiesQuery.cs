using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetPortCallActivitiesQuery(DbContext dbContext) : IGetPortCallActivitiesQuery
{
    public async Task<PortCallActivitiesResponse> GetActivitiesAsync(
        Guid voyageId,
        Guid stopId,
        GetPortCallActivitiesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortCallLogValidator.ValidatePaging(request);
        if (errors.Count > 0)
        {
            throw new PortCallLogValidationException(errors);
        }

        await PortCallLog.LoadStopAsync(dbContext, voyageId, stopId, tracking: false, cancellationToken);

        var pageNumber = request.PageNumber ?? PortCallLogValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? PortCallLogValidator.DefaultPageSize;

        var activities = dbContext.Set<PortCallActivity>()
            .AsNoTracking()
            .Where(activity => activity.VoyageStopId == stopId);

        var totalCount = await activities.CountAsync(cancellationToken);

        var page = await activities
            .OrderBy(activity => activity.OccurredAtUtc)
            .ThenBy(activity => activity.Sequence)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PortCallActivitiesResponse(
            voyageId,
            stopId,
            page.Select(PortCallLog.ToItem).ToList(),
            pageNumber,
            pageSize,
            totalCount);
    }

    public async Task<PortCallActivityDetailsResponse> GetActivityAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        await PortCallLog.LoadStopAsync(dbContext, voyageId, stopId, tracking: false, cancellationToken);

        var activity = await dbContext.Set<PortCallActivity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == activityId && candidate.VoyageStopId == stopId,
                cancellationToken)
            ?? throw new PortCallActivityNotFoundException(activityId);

        var history = await dbContext.Set<PortCallActivityRevision>()
            .AsNoTracking()
            .Where(revision => revision.PortCallActivityId == activityId)
            .OrderBy(revision => revision.Revision)
            .Select(revision => new PortCallActivityRevisionItem(
                revision.Revision,
                revision.ChangeType,
                revision.ActivityType,
                revision.OccurredAtUtc,
                revision.EndedAtUtc,
                revision.Notes,
                revision.Reason,
                revision.ActorUserId,
                revision.ActorName,
                revision.ChangedAtUtc))
            .ToListAsync(cancellationToken);

        return new PortCallActivityDetailsResponse(
            voyageId,
            stopId,
            PortCallLog.ToItem(activity),
            history);
    }

    public async Task<PortCallTimelineResponse> GetTimelineAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        var (voyageNumber, stop) = await PortCallLog.LoadStopAsync(
            dbContext,
            voyageId,
            stopId,
            tracking: false,
            cancellationToken);

        var sources = await PortCallLog.LoadSourcesAsync(
            dbContext,
            voyageNumber,
            stop,
            tracking: false,
            cancellationToken);

        var statement = await PortCallLog.FindStatementAsync(
            dbContext,
            stopId,
            tracking: false,
            cancellationToken);

        var activities = sources.Activities
            .OrderBy(activity => activity.OccurredAtUtc)
            .ThenBy(activity => activity.Sequence)
            .Select(PortCallLog.ToItem)
            .ToList();

        return new PortCallTimelineResponse(
            voyageId,
            stopId,
            stop.Status,
            sources.PortName,
            sources.PortTimeZone,
            sources.BerthName,
            stop.Ata,
            stop.Atd,
            stop.Ata is not null && statement?.IsFinalized != true,
            statement is null
                ? null
                : new PortCallStatementSummary(
                    statement.Id,
                    statement.Status,
                    statement.FinalizedByUserId,
                    statement.FinalizedAtUtc),
            PortCallLog.ToEntryItems(PortCallLog.BuildEntries(sources), stop.Ata, stop.Atd),
            activities);
    }
}
