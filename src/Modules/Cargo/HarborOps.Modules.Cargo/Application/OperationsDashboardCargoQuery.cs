using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class OperationsDashboardCargoQuery(DbContext dbContext)
    : IOperationsDashboardCargoQuery
{
    public async Task<OperationsDashboardCargoResult> GetOpenHazardousBookingsAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var bookings = dbContext.Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.IsHazardous && booking.Status != CargoStatus.Completed);

        var openHazardousBookingCount = await bookings.CountAsync(cancellationToken);
        var items = await bookings
            .OrderBy(booking => booking.BookingNumber)
            .ThenBy(booking => booking.Id)
            .Take(maximumItems)
            .Select(booking => new OperationsDashboardCargoItem(
                booking.Id,
                booking.BookingNumber,
                booking.CargoType,
                booking.WeightKg,
                booking.Status.ToString()))
            .ToListAsync(cancellationToken);

        return new OperationsDashboardCargoResult(openHazardousBookingCount, items);
    }
}
