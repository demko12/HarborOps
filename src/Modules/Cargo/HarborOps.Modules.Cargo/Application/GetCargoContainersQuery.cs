using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoContainersQuery(DbContext dbContext)
{
    public Task<GetCargoContainersResponse> GetContainersAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken) =>
        GetContainersAsync(cargoBookingId, new GetCargoContainersRequest(), cancellationToken);

    public async Task<GetCargoContainersResponse> GetContainersAsync(
        Guid cargoBookingId,
        GetCargoContainersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetCargoContainersValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetCargoContainersValidationException(errors);
        }

        var filter = GetCargoContainersValidator.ToFilter(request);
        var pageNumber = request.PageNumber ?? GetCargoContainersValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetCargoContainersValidator.DefaultPageSize;

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

        var counts = await containers
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalCount = group.Count(),
                LoadedCount = group.Count(container => container.Status == CargoStatus.Loaded),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalCount = counts?.TotalCount ?? 0;
        var loadedCount = counts?.LoadedCount ?? 0;

        var matchingContainers = filter.Apply(containers);
        var matchingCount = filter.IsEmpty
            ? totalCount
            : await matchingContainers.CountAsync(cancellationToken);

        var items = await matchingContainers
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

        return new GetCargoContainersResponse(
            items,
            totalCount,
            pageNumber,
            pageSize,
            loadedCount,
            matchingCount);
    }
}
