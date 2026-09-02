using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RecordDelayService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordDelayResponse> RecordAsync(
        Guid voyageId,
        RecordDelayRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RecordDelayValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RecordDelayValidationException(errors);
        }

        var voyageExists = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .AnyAsync(voyage => voyage.Id == voyageId, cancellationToken);

        if (!voyageExists)
        {
            throw new VoyageNotFoundException(voyageId);
        }

        if (request.VoyageStopId is { } voyageStopId)
        {
            var stopExists = await dbContext.Set<VoyageStop>()
                .AsNoTracking()
                .AnyAsync(
                    stop => stop.Id == voyageStopId && stop.VoyageId == voyageId,
                    cancellationToken);

            if (!stopExists)
            {
                throw new VoyageStopNotFoundException(voyageStopId);
            }
        }

        var createdAt = timeProvider.GetUtcNow().UtcDateTime;
        var reason = request.Reason!.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var delay = Delay.Create(
            voyageId,
            reason,
            request.Duration!.Value,
            createdAt,
            request.VoyageStopId,
            description);

        await dbContext.Set<Delay>().AddAsync(delay, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordDelayResponse(
            delay.Id,
            delay.VoyageId,
            delay.VoyageStopId,
            delay.Reason,
            delay.Description,
            delay.Duration,
            delay.CreatedAt);
    }
}
