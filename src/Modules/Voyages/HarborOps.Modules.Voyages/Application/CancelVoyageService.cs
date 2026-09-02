using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class CancelVoyageService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<VoyageScheduleResponse> CancelAsync(
        Guid voyageId,
        Guid actingUserId,
        CancelVoyageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CancelVoyageValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CancelVoyageValidationException(errors);
        }

        var reason = request.Reason!.Trim();

        var voyage = await dbContext.Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        var previousStatus = voyage.Status;

        voyage.Cancel();

        var change = VoyageScheduleChange.RecordCancellation(
            voyage.Id,
            reason,
            actingUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            previousStatus,
            voyage.Status,
            voyage.RouteId,
            voyage.DeparturePortId,
            voyage.ArrivalPortId,
            voyage.PlannedDeparture,
            voyage.PlannedArrival);

        await dbContext.Set<VoyageScheduleChange>().AddAsync(change, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await VoyageScheduleProjection.ReadAsync(dbContext, voyageId, cancellationToken);
    }
}
