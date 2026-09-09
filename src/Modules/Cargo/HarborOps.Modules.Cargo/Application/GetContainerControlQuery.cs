using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetContainerControlQuery(DbContext dbContext)
{
    public async Task<GetContainerControlResponse> GetAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        var container = await ContainerControlGuard.FindContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var control = await dbContext
            .Set<ContainerControl>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ContainerId == containerId, cancellationToken);

        var details = ContainerControlMapper.ToDetails(container, control);

        if (control is null)
        {
            return new GetContainerControlResponse(details, []);
        }

        var history = await dbContext
            .Set<ContainerControlEvent>()
            .AsNoTracking()
            .Where(entry => entry.ContainerControlId == control.Id)
            .OrderBy(entry => entry.OccurredUtc)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

        return new GetContainerControlResponse(
            details,
            [.. history.Select(ContainerControlMapper.ToListItem)]);
    }
}
