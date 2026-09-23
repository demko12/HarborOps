using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal static class PortCallActivityChange
{
    internal static async Task<PortCallActivity> LoadForChangeAsync(
        DbContext dbContext,
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        Guid expectedVersion,
        CancellationToken cancellationToken)
    {
        await PortCallLog.LoadStopAsync(
            dbContext,
            voyageId,
            stopId,
            tracking: false,
            cancellationToken);

        var activity = await dbContext.Set<PortCallActivity>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == activityId && candidate.VoyageStopId == stopId,
                cancellationToken)
            ?? throw new PortCallActivityNotFoundException(activityId);

        if (activity.IsLocked)
        {
            throw new PortCallActivityLockedException(activity.Id);
        }

        var statement = await PortCallLog.FindStatementAsync(
            dbContext,
            stopId,
            tracking: false,
            cancellationToken);

        statement?.EnsureDraft();

        if (activity.ConcurrencyToken != expectedVersion)
        {
            throw new PortCallLogConflictException(stopId);
        }

        return activity;
    }
}
