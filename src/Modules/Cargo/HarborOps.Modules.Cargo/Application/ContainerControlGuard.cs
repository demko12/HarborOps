using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal static class ContainerControlGuard
{
    internal static async Task<Container> FindContainerAsync(
        DbContext dbContext,
        Guid containerId,
        CancellationToken cancellationToken)
    {
        var container = await dbContext
            .Set<Container>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == containerId, cancellationToken);

        return container ?? throw new ContainerNotFoundException(containerId);
    }

    internal static async Task<ContainerControl?> FindControlAsync(
        DbContext dbContext,
        Guid containerId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<ContainerControl>()
            .SingleOrDefaultAsync(control => control.ContainerId == containerId, cancellationToken);
    }

    internal static async Task<ContainerControl> GetOrStartControlAsync(
        DbContext dbContext,
        Guid containerId,
        CancellationToken cancellationToken)
    {
        var control = await FindControlAsync(dbContext, containerId, cancellationToken);

        if (control is not null)
        {
            return control;
        }

        var started = ContainerControl.Start(containerId);
        await dbContext.Set<ContainerControl>().AddAsync(started, cancellationToken);

        return started;
    }

    internal static async Task EnsureContainersAreReleasedAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var containers = await dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == cargoBookingId)
            .Select(container => new { container.Id, container.ContainerNumber })
            .ToListAsync(cancellationToken);

        if (containers.Count == 0)
        {
            return;
        }

        var containerIds = containers.ConvertAll(container => container.Id);

        var controlStates = await dbContext
            .Set<ContainerControl>()
            .AsNoTracking()
            .Where(control => containerIds.Contains(control.ContainerId))
            .Select(control => new { control.ContainerId, control.State })
            .ToListAsync(cancellationToken);

        var blockedContainerIds = await ReeferMonitoringGuard
            .GetContainerIdsWithOpenExcursionsAsync(dbContext, containerIds, cancellationToken);

        var releasedContainerIds = controlStates
            .Where(control => ContainerControl.AllowsLoading(
                control.State,
                blockedContainerIds.Contains(control.ContainerId)))
            .Select(control => control.ContainerId)
            .ToList();

        var blockingContainerNumbers = containers
            .Where(container => !releasedContainerIds.Contains(container.Id))
            .Select(container => container.ContainerNumber)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (blockingContainerNumbers.Count > 0)
        {
            throw new CargoContainersNotReleasedException(cargoBookingId, blockingContainerNumbers);
        }
    }

    internal static async Task EnsureActiveSealIsUniqueAsync(
        DbContext dbContext,
        Guid containerId,
        string normalizedSealNumber,
        CancellationToken cancellationToken)
    {
        var isTaken = await dbContext
            .Set<ContainerControl>()
            .AsNoTracking()
            .AnyAsync(
                control => control.ContainerId != containerId
                    && control.SealNumber == normalizedSealNumber
                    && control.SealStatus == ContainerSealStatus.Intact,
                cancellationToken);

        if (isTaken)
        {
            throw new DuplicateActiveSealException(normalizedSealNumber);
        }
    }
}
