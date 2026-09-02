using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RecordStopDepartureService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordStopDepartureResponse> RecordDepartureAsync(
        Guid voyageId,
        Guid stopId,
        RecordStopDepartureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = RecordStopDepartureValidator.Validate(request, utcNow);
        if (errors.Count > 0)
        {
            throw new RecordStopDepartureValidationException(errors);
        }

        var actualDepartureUtc = request.ActualDepartureUtc!.Value;

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

        if (stop.Ata is { } actualArrival)
        {
            var chronologyErrors = RecordStopDepartureValidator.ValidateAgainstActualArrival(
                actualDepartureUtc,
                actualArrival);

            if (chronologyErrors.Count > 0)
            {
                throw new RecordStopDepartureValidationException(chronologyErrors);
            }
        }

        stop.RecordDeparture(actualDepartureUtc);

        var activeSchedule = await dbContext.Set<BerthSchedule>()
            .FirstOrDefaultAsync(
                schedule => schedule.VoyageStopId == stopId
                    && schedule.Status == BerthSchedule.OccupiedStatus,
                cancellationToken);

        activeSchedule?.Release();

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordStopDepartureResponse(stop.Id, actualDepartureUtc, stop.Status);
    }
}
