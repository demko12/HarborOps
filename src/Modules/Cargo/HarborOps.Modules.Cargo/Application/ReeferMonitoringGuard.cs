using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal static class ReeferMonitoringGuard
{
    internal static async Task<Container> FindReeferContainerAsync(
        DbContext dbContext,
        Guid containerId,
        CancellationToken cancellationToken)
    {
        var container = await ContainerControlGuard.FindContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        if (!container.IsReefer)
        {
            throw new ContainerIsNotReeferException(containerId);
        }

        return container;
    }

    internal static async Task<ReeferMonitoringProfile?> FindProfileAsync(
        DbContext dbContext,
        Guid containerId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var profiles = dbContext.Set<ReeferMonitoringProfile>();

        return tracked
            ? await profiles.SingleOrDefaultAsync(
                profile => profile.ContainerId == containerId,
                cancellationToken)
            : await profiles.AsNoTracking().SingleOrDefaultAsync(
                profile => profile.ContainerId == containerId,
                cancellationToken);
    }

    internal static async Task<ReeferMonitoringProfile> RequireProfileAsync(
        DbContext dbContext,
        Guid containerId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var profile = await FindProfileAsync(dbContext, containerId, tracked, cancellationToken);

        return profile ?? throw new ReeferProfileNotFoundException(containerId);
    }

    internal static async Task<ReeferTemperatureReading> FindReadingAsync(
        DbContext dbContext,
        Guid containerId,
        Guid readingId,
        CancellationToken cancellationToken)
    {
        var reading = await dbContext
            .Set<ReeferTemperatureReading>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == readingId && candidate.ContainerId == containerId,
                cancellationToken);

        return reading ?? throw new ReeferReadingNotFoundException(readingId);
    }

    internal static async Task<ReeferExcursion> FindExcursionAsync(
        DbContext dbContext,
        Guid containerId,
        Guid excursionId,
        CancellationToken cancellationToken)
    {
        var excursion = await dbContext
            .Set<ReeferExcursion>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == excursionId && candidate.ContainerId == containerId,
                cancellationToken);

        return excursion ?? throw new ReeferExcursionNotFoundException(excursionId);
    }

    internal static async Task<ReeferExcursion?> FindOpenExcursionForReadingAsync(
        DbContext dbContext,
        Guid readingId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<ReeferExcursion>()
            .SingleOrDefaultAsync(
                excursion => excursion.ReadingId == readingId
                    && excursion.Status == ReeferExcursionStatus.Open,
                cancellationToken);
    }

    internal static async Task<bool> HasOpenExcursionAsync(
        DbContext dbContext,
        Guid containerId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .AnyAsync(
                excursion => excursion.ContainerId == containerId
                    && excursion.Status == ReeferExcursionStatus.Open,
                cancellationToken);
    }

    internal static async Task<HashSet<Guid>> GetContainerIdsWithOpenExcursionsAsync(
        DbContext dbContext,
        IReadOnlyCollection<Guid> containerIds,
        CancellationToken cancellationToken)
    {
        if (containerIds.Count == 0)
        {
            return [];
        }

        var blocked = await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .Where(excursion => containerIds.Contains(excursion.ContainerId)
                && excursion.Status == ReeferExcursionStatus.Open)
            .Select(excursion => excursion.ContainerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. blocked];
    }
}
