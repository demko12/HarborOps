using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoContainersQuery(DbContext dbContext)
{
    internal const int DefaultPageSize = 15;
    internal const int MaximumPageSize = 15;

    public Task<GetCargoContainersResponse> GetContainersAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken) =>
        GetContainersAsync(cargoBookingId, 1, DefaultPageSize, cancellationToken);

    public async Task<GetCargoContainersResponse> GetContainersAsync(
        Guid cargoBookingId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaximumPageSize)
        {
            throw new CargoContainerPageValidationException();
        }
        var bookingExists = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .AnyAsync(booking => booking.Id == cargoBookingId, cancellationToken);

        if (!bookingExists)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        var containers = dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == cargoBookingId);
        var totalCount = await containers.CountAsync(cancellationToken);
        var loadedCount = await containers.CountAsync(container => container.Status == CargoStatus.Loaded, cancellationToken);
        var items = await containers
            .OrderBy(container => container.ContainerNumber)
            .ThenBy(container => container.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(container => new CargoContainerListItem(
                container.Id,
                container.ContainerNumber,
                container.ContainerType,
                container.Status.ToString(),
                container.CargoBookingId!.Value,
                container.VesselId,
                container.PortOfLoadingCode,
                container.PortOfDischargeCode,
                container.WeightKg))
            .ToListAsync(cancellationToken);

        return new GetCargoContainersResponse(items, totalCount, pageNumber, pageSize, loadedCount);
    }
}

internal sealed class CargoContainerPageValidationException : Exception;
