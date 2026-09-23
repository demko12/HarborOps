using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetMasterDataSummaryQuery(DbContext dbContext) : IGetMasterDataSummaryQuery
{
    public async Task<MasterDataSummaryResponse> GetAsync(CancellationToken cancellationToken)
    {
        var portCount = await dbContext.Set<Port>().CountAsync(cancellationToken);
        var berthCount = await dbContext.Set<Berth>().CountAsync(cancellationToken);
        var routeCount = await dbContext.Set<Route>().CountAsync(cancellationToken);
        var averageRouteDistance = await dbContext.Set<Route>()
            .AverageAsync(route => (decimal?)route.EstimatedDistance, cancellationToken);

        return new MasterDataSummaryResponse(
            portCount,
            berthCount,
            routeCount,
            averageRouteDistance ?? 0m);
    }
}
