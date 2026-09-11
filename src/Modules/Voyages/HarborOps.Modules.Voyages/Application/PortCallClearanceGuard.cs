using System.Data;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HarborOps.Modules.Voyages.Application;

internal static class PortCallClearanceGuard
{
    internal static Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.IsRelational()
            ? BeginAsync(dbContext, cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

        static async Task<IDbContextTransaction?> BeginAsync(
            DbContext dbContext,
            CancellationToken cancellationToken)
        {
            return await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        }
    }

    internal static async Task<VoyageStop> LoadStopAsync(
        DbContext dbContext,
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        var voyageExists = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .AnyAsync(voyage => voyage.Id == voyageId, cancellationToken);

        if (!voyageExists)
        {
            throw new VoyageNotFoundException(voyageId);
        }

        return await dbContext.Set<VoyageStop>()
            .SingleOrDefaultAsync(
                stop => stop.Id == stopId && stop.VoyageId == voyageId,
                cancellationToken)
            ?? throw new VoyageStopNotFoundException(stopId);
    }

    internal static void EnsureStopHasNotDeparted(VoyageStop stop)
    {
        if (stop.Atd is not null
            || string.Equals(stop.Status, VoyageStop.DepartedStatus, StringComparison.Ordinal))
        {
            throw new VoyageStopAlreadyDepartedException(stop.Id);
        }
    }

    internal static async Task<PortCallClearance> LoadClearanceAsync(
        DbContext dbContext,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<PortCallClearance>()
            .SingleOrDefaultAsync(clearance => clearance.VoyageStopId == stopId, cancellationToken)
            ?? throw new PortCallClearanceNotFoundException(stopId);
    }

    internal static async Task<PortCallClearanceItem> LoadItemAsync(
        DbContext dbContext,
        Guid clearanceId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<PortCallClearanceItem>()
            .SingleOrDefaultAsync(
                item => item.Id == itemId && item.PortCallClearanceId == clearanceId,
                cancellationToken)
            ?? throw new PortCallClearanceItemNotFoundException(itemId);
    }

    internal static async Task<List<PortCallClearanceItem>> LoadItemsAsync(
        DbContext dbContext,
        Guid clearanceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<PortCallClearanceItem>()
            .Where(item => item.PortCallClearanceId == clearanceId)
            .ToListAsync(cancellationToken);
    }
}
