using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoBookingGuard
{
    internal static async Task EnsureOpenAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var status = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.Id == cargoBookingId)
            .Select(booking => (CargoStatus?)booking.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        if (status == CargoStatus.Completed)
        {
            throw new CargoBookingCompletedException(cargoBookingId);
        }
    }
}
