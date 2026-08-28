using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RecordStopArrivalService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordStopArrivalResponse> RecordArrivalAsync(
        Guid voyageId,
        Guid stopId,
        RecordStopArrivalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = RecordStopArrivalValidator.Validate(request, utcNow);
        if (errors.Count > 0)
        {
            throw new RecordStopArrivalValidationException(errors);
        }

        var actualArrivalUtc = request.ActualArrivalUtc!.Value;

        var voyage = await dbContext.Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        if (!string.Equals(voyage.Status, Voyage.InProgressStatus, StringComparison.Ordinal))
        {
            throw new VoyageNotExecutingException(voyageId, voyage.Status);
        }

        var stop = await dbContext.Set<VoyageStop>()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == stopId && candidate.VoyageId == voyageId,
                cancellationToken)
            ?? throw new VoyageStopNotFoundException(stopId);

        if (stop.StopOrder > 1)
        {
            var previousStop = await dbContext.Set<VoyageStop>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.VoyageId == voyageId
                        && candidate.StopOrder == stop.StopOrder - 1,
                    cancellationToken);

            if (previousStop is not null
                && !string.Equals(previousStop.Status, VoyageStop.DepartedStatus, StringComparison.Ordinal))
            {
                throw new VoyageStopOutOfSequenceException(stop.Id, stop.StopOrder);
            }
        }

        stop.RecordArrival(actualArrivalUtc);

        var activeSchedule = await dbContext.Set<BerthSchedule>()
            .FirstOrDefaultAsync(
                schedule => schedule.VoyageStopId == stopId
                    && schedule.Status == BerthSchedule.ReservedStatus,
                cancellationToken);

        activeSchedule?.MarkOccupied();

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordStopArrivalResponse(stop.Id, actualArrivalUtc, stop.Status);
    }
}
