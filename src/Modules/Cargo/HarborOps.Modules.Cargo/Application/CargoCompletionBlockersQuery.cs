using HarborOps.Modules.Cargo.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class CargoCompletionBlockersQuery(DbContext dbContext)
{
    public async Task<CargoCompletionBlockers> GetAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        await CargoDiscrepancyGuard.FindBookingAsync(dbContext, cargoBookingId, tracked: false, cancellationToken);

        return await CargoDiscrepancyGuard.GetCompletionBlockersAsync(dbContext, cargoBookingId, cancellationToken);
    }
}
