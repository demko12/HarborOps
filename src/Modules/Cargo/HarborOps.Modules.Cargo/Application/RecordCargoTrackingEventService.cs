using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class RecordCargoTrackingEventService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RecordCargoTrackingEventResponse> RecordAsync(
        Guid cargoBookingId,
        Guid responsibleUserId,
        RecordCargoTrackingEventRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackingEvents = dbContext.Set<CargoTrackingEvent>();

        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == cargoBookingId,
                cancellationToken);

        if (booking is null)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        var latestEventOccurredUtc = await trackingEvents
            .Where(trackingEvent => trackingEvent.CargoBookingId == cargoBookingId)
            .OrderByDescending(trackingEvent => trackingEvent.OccurredUtc)
            .Select(trackingEvent => (DateTime?)trackingEvent.OccurredUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var currentUtc = timeProvider.GetUtcNow().UtcDateTime;
        var errors = RecordCargoTrackingEventValidator.Validate(
            request,
            currentUtc,
            booking.CreatedUtc,
            latestEventOccurredUtc);

        if (errors.Count > 0)
        {
            throw new RecordCargoTrackingEventValidationException(errors);
        }

        RecordCargoTrackingEventValidator.TryParseStatus(request.Status, out var requestedStatus);

        if (booking.IsHazardous && requestedStatus == CargoStatus.Loaded)
        {
            await CargoSafetyInfoGuard.EnsureApprovedForLoadingAsync(
                dbContext,
                cargoBookingId,
                cancellationToken);
        }

        if (requestedStatus == CargoStatus.Completed && booking.NextStatus == CargoStatus.Completed)
        {
            var blockers = await CargoDiscrepancyGuard.GetCompletionBlockersAsync(
                dbContext,
                cargoBookingId,
                cancellationToken);

            if (blockers.OpenCount > 0)
            {
                throw new CargoBookingCompletionBlockedException(blockers);
            }
        }

        try
        {
            booking.AdvanceStatusTo(requestedStatus);
        }
        catch (CargoStatusTransitionNotAllowedException exception)
        {
            throw new CargoTrackingEventTransitionException(
                exception.CargoBookingId,
                exception.CurrentStatus,
                exception.RequestedStatus);
        }

        var trackingEvent = CargoTrackingEvent.Record(
            booking.Id,
            responsibleUserId,
            requestedStatus,
            request.OccurredUtc!.Value,
            request.Location!.Trim());

        await trackingEvents.AddAsync(trackingEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordCargoTrackingEventResponse(
            trackingEvent.Id,
            trackingEvent.Status.ToString());
    }
}
