using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class GetVesselTypesQuery(DbContext dbContext) : IGetVesselTypesQuery
{
    public async Task<VesselTypesResponse> GetAsync(CancellationToken cancellationToken)
    {
        var vesselTypes = await dbContext.Set<VesselType>()
            .AsNoTracking()
            .OrderBy(vesselType => vesselType.Name)
            .ThenBy(vesselType => vesselType.Id)
            .Select(vesselType => new VesselTypeListItem(
                vesselType.Id,
                vesselType.Name,
                vesselType.Code))
            .ToListAsync(cancellationToken);

        return new VesselTypesResponse(vesselTypes);
    }
}
