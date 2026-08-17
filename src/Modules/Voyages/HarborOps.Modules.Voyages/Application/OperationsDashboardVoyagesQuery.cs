using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class OperationsDashboardVoyagesQuery(DbContext dbContext)
    : IOperationsDashboardVoyagesQuery
{
    public async Task<OperationsDashboardVoyagesResult> GetUpcomingVoyagesAsync(
        DateTime asOfUtc,
        DateTime throughUtc,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var voyages = dbContext.Set<Voyage>()
            .AsNoTracking()
            .Where(voyage =>
                (voyage.Status == Voyage.PlannedStatus && voyage.PlannedDeparture >= asOfUtc && voyage.PlannedDeparture <= throughUtc)
                || (voyage.Status == Voyage.InProgressStatus && voyage.PlannedArrival >= asOfUtc && voyage.PlannedArrival <= throughUtc));

        var upcomingVoyageCount = await voyages.CountAsync(cancellationToken);
        var items = await voyages
            .OrderBy(voyage => voyage.PlannedDeparture)
            .ThenBy(voyage => voyage.Id)
            .Take(maximumItems)
            .Select(voyage => new OperationsDashboardVoyageItem(
                voyage.Id,
                voyage.VoyageNumber,
                voyage.DeparturePortCode,
                voyage.ArrivalPortCode,
                voyage.Status,
                voyage.PlannedDeparture))
            .ToListAsync(cancellationToken);

        return new OperationsDashboardVoyagesResult(upcomingVoyageCount, items);
    }
}
