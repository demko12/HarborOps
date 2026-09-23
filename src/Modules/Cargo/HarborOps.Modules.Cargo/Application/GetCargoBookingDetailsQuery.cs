using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoBookingDetailsQuery(DbContext dbContext)
{
    public async Task<GetCargoBookingDetailsResponse> GetDetailsAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var booking = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.Id == cargoBookingId)
            .Select(booking => new
            {
                booking.Id,
                booking.BookingNumber,
                booking.VoyageId,
                booking.Description,
                booking.CargoType,
                booking.WeightKg,
                booking.PackageCount,
                booking.IsHazardous,
                booking.Status,
                booking.CreatedUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (booking is null)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        var safetyInfo = await dbContext
            .Set<CargoSafetyInfo>()
            .AsNoTracking()
            .Where(safetyInfo => safetyInfo.CargoBookingId == cargoBookingId)
                        .Select(safetyInfo => new CargoSafetyInfoDetails(
                safetyInfo.Id,
                safetyInfo.ImdgClass,
                safetyInfo.UnNumber,
                safetyInfo.StorageConditions,
                safetyInfo.HandlingNotes,
                safetyInfo.ReviewStatus.ToString(),
                safetyInfo.SubmittedByUserId,
                safetyInfo.SubmittedAtUtc,
                safetyInfo.ReviewedByUserId,
                safetyInfo.ReviewedAtUtc,
                safetyInfo.ReviewNotes))
            .SingleOrDefaultAsync(cancellationToken);

        var trackingEvents = await dbContext
            .Set<CargoTrackingEvent>()
            .AsNoTracking()
            .Where(trackingEvent => trackingEvent.CargoBookingId == cargoBookingId)
            .OrderBy(trackingEvent => trackingEvent.OccurredUtc)
            .ThenBy(trackingEvent => trackingEvent.Id)
            .Select(trackingEvent => new CargoTrackingEventListItem(
                trackingEvent.Id,
                trackingEvent.Status.ToString(),
                trackingEvent.OccurredUtc,
                trackingEvent.Location,
                trackingEvent.ResponsibleUserId))
            .ToListAsync(cancellationToken);

        var containerControls = await GetContainerControlsAsync(cargoBookingId, cancellationToken);

        return new GetCargoBookingDetailsResponse(
            booking.Id,
            booking.BookingNumber,
            booking.VoyageId,
            booking.Description,
            booking.CargoType,
            booking.WeightKg,
            booking.PackageCount,
            booking.IsHazardous,
            booking.Status.ToString(),
            CargoBooking.NextStatusFor(booking.Status)?.ToString(),
            booking.CreatedUtc,
            safetyInfo,
            trackingEvents,
            containerControls);
    }

    private async Task<IReadOnlyList<ContainerControlDetails>> GetContainerControlsAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var containers = await dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == cargoBookingId)
            .OrderBy(container => container.ContainerNumber)
            .ToListAsync(cancellationToken);

        if (containers.Count == 0)
        {
            return [];
        }

        var containerIds = containers.ConvertAll(container => container.Id);

        var controls = await dbContext
            .Set<ContainerControl>()
            .AsNoTracking()
            .Where(control => containerIds.Contains(control.ContainerId))
            .ToListAsync(cancellationToken);

        var blockedContainerIds = await ReeferMonitoringGuard
            .GetContainerIdsWithOpenExcursionsAsync(dbContext, containerIds, cancellationToken);

        return
        [
            .. containers.Select(container => ContainerControlMapper.ToDetails(
                container,
                controls.Find(control => control.ContainerId == container.Id),
                blockedContainerIds.Contains(container.Id))),
        ];
    }
}
