using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoContainersQuery(DbContext dbContext)
{
    public async Task<GetCargoContainersResponse> GetContainersAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var bookingExists = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .AnyAsync(booking => booking.Id == cargoBookingId, cancellationToken);

        if (!bookingExists)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        var items = await dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == cargoBookingId)
            .OrderBy(container => container.ContainerNumber)
            .ThenBy(container => container.Id)
            .Select(container => new CargoContainerListItem(
                container.Id,
                container.ContainerNumber,
                container.ContainerType,
                container.Status.ToString(),
                container.CargoBookingId!.Value,
                container.VesselId))
            .ToListAsync(cancellationToken);

        return new GetCargoContainersResponse(items);
    }
}
