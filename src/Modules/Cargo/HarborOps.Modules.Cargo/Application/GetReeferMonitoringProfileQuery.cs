using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetReeferMonitoringProfileQuery(DbContext dbContext)
{
    public async Task<GetReeferMonitoringProfileResponse> GetAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        var container = await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var profile = await ReeferMonitoringGuard.FindProfileAsync(
            dbContext,
            containerId,
            tracked: false,
            cancellationToken);

        var changes = new List<ReeferProfileChange>();

        if (profile is not null)
        {
            changes = await dbContext
                .Set<ReeferProfileChange>()
                .AsNoTracking()
                .Where(change => change.ReeferMonitoringProfileId == profile.Id)
                .OrderByDescending(change => change.Version)
                .ThenBy(change => change.Id)
                .ToListAsync(cancellationToken);
        }

        var openExcursionCount = await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .CountAsync(
                excursion => excursion.ContainerId == containerId
                    && excursion.Status == ReeferExcursionStatus.Open,
                cancellationToken);

        var latestInspectionTemperatureCelsius = await dbContext
            .Set<ContainerControl>()
            .AsNoTracking()
            .Where(control => control.ContainerId == containerId)
            .Select(control => control.LatestInspectionTemperatureCelsius)
            .SingleOrDefaultAsync(cancellationToken);

        return new GetReeferMonitoringProfileResponse(
            container.Id,
            container.ContainerNumber,
            profile is null ? null : ReeferMonitoringMapper.ToDetails(profile),
            [.. changes.Select(ReeferMonitoringMapper.ToListItem)],
            openExcursionCount,
            latestInspectionTemperatureCelsius);
    }
}
