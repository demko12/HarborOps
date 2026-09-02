using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class SearchCargoBookingsQuery(DbContext dbContext)
{
    public async Task<SearchCargoBookingsResponse> SearchAsync(
        SearchCargoBookingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchCargoBookingsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchCargoBookingsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? SearchCargoBookingsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? SearchCargoBookingsValidator.DefaultPageSize;

        var bookings = dbContext.Set<CargoBooking>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();

            bookings = bookings.Where(booking =>
                booking.BookingNumber.Contains(search)
                || booking.Description.Contains(search));
        }

        if (SearchCargoBookingsValidator.TryParseStatus(request.Status, out var status))
        {
            bookings = bookings.Where(booking => booking.Status == status);
        }

        if (request.IsHazardous is { } isHazardous)
        {
            bookings = bookings.Where(booking => booking.IsHazardous == isHazardous);
        }

        var totalCount = await bookings.CountAsync(cancellationToken);
        var hazardousCount = await bookings.CountAsync(
            booking => booking.IsHazardous,
            cancellationToken);

        var items = await bookings
            .OrderBy(booking => booking.Status == CargoStatus.InTransit
                ? 0
                : booking.Status == CargoStatus.Arrived
                    ? 1
                    : booking.Status == CargoStatus.Loaded
                        ? 2
                        : booking.Status == CargoStatus.Unloaded
                            ? 3
                            : booking.Status == CargoStatus.Planned
                                ? 4
                                : booking.Status == CargoStatus.Registered
                                    ? 5
                                    : booking.Status == CargoStatus.Completed
                                        ? 6
                                        : 7)
            .ThenByDescending(booking => booking.CreatedUtc)
            .ThenBy(booking => booking.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(booking => new CargoBookingListItem(
                booking.Id,
                booking.BookingNumber,
                booking.VoyageId,
                booking.Description,
                booking.CargoType,
                booking.WeightKg,
                booking.PackageCount,
                booking.IsHazardous,
                booking.Status.ToString(),
                booking.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new SearchCargoBookingsResponse(
            items,
            pageNumber,
            pageSize,
            totalCount,
            hazardousCount);
    }
}
