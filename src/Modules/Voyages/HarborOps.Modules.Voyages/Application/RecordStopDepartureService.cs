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

        var transaction = await PortCallClearanceGuard.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);

        try
        {
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

            if (!stop.IsAwaitingDeparture)
            {
                throw new VoyageStopNotArrivedException(stop.Id, stop.Status);
            }

            await EnsureClearanceAllowsDepartureAsync(stopId, cancellationToken);

            stop.RecordDeparture(actualDepartureUtc);

            var activeSchedule = await dbContext.Set<BerthSchedule>()
                .FirstOrDefaultAsync(
                    schedule => schedule.VoyageStopId == stopId
                        && schedule.Status == BerthSchedule.OccupiedStatus,
                    cancellationToken);

            activeSchedule?.Release();

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new RecordStopDepartureResponse(stop.Id, actualDepartureUtc, stop.Status);
        }
        catch (DbUpdateException exception)
        {
            throw new PortCallClearanceConflictException(stopId, exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task EnsureClearanceAllowsDepartureAsync(
        Guid stopId,
        CancellationToken cancellationToken)
    {
        var clearance = await dbContext.Set<PortCallClearance>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.VoyageStopId == stopId, cancellationToken);

        var requiredItemCount = 0;
        var unresolvedRequiredItemCount = 0;

        if (clearance is not null)
        {
            var requiredItems = await dbContext.Set<PortCallClearanceItem>()
                .AsNoTracking()
                .Where(item => item.PortCallClearanceId == clearance.Id && item.IsRequired)
                .Select(item => item.Status)
                .ToListAsync(cancellationToken);

            requiredItemCount = requiredItems.Count;
            unresolvedRequiredItemCount = requiredItems.Count(status => string.Equals(
                status,
                PortCallClearanceItem.PendingStatus,
                StringComparison.Ordinal));
        }

        if (clearance is not null && clearance.AllowsDeparture && unresolvedRequiredItemCount == 0)
        {
            return;
        }

        throw new VoyageStopClearanceNotCompletedException(
            stopId,
            clearance?.Status ?? PortCallClearance.PendingStatus,
            requiredItemCount,
            unresolvedRequiredItemCount);
    }
}
