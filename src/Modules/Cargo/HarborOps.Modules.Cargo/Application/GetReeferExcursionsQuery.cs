using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetReeferExcursionsQuery(DbContext dbContext)
{
    public async Task<GetReeferExcursionsResponse> GetAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var excursions = await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .Where(excursion => excursion.ContainerId == containerId)
            .OrderByDescending(excursion => excursion.DetectedAtUtc)
            .ThenBy(excursion => excursion.Id)
            .ToListAsync(cancellationToken);

        var openCount = excursions.Count(excursion =>
            excursion.Status == ReeferExcursionStatus.Open);

        return new GetReeferExcursionsResponse(
            [.. excursions.Select(ReeferMonitoringMapper.ToListItem)],
            openCount);
    }

    public async Task<ReeferExcursionListItem> GetDetailsAsync(
        Guid containerId,
        Guid excursionId,
        CancellationToken cancellationToken)
    {
        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var excursion = await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == excursionId
                    && candidate.ContainerId == containerId,
                cancellationToken)
            ?? throw new ReeferExcursionNotFoundException(excursionId);

        return ReeferMonitoringMapper.ToListItem(excursion);
    }
}
