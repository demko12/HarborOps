using HarborOps.Modules.Voyages.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetVoyageScheduleChangesQuery(DbContext dbContext)
    : IGetVoyageScheduleChangesQuery
{
    public Task<VoyageScheduleResponse> GetScheduleChangesAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        return VoyageScheduleProjection.ReadAsync(dbContext, voyageId, cancellationToken);
    }
}
