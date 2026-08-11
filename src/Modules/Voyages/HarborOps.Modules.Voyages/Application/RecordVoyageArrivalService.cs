using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RecordVoyageArrivalService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordVoyageArrivalResponse> RecordArrivalAsync(
        Guid voyageId,
        RecordVoyageArrivalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = RecordVoyageArrivalValidator.Validate(request, utcNow);
        if (errors.Count > 0)
        {
            throw new RecordVoyageArrivalValidationException(errors);
        }

        var actualArrivalUtc = request.ActualArrivalUtc!.Value;

        var voyage = await dbContext
            .Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        if (voyage.ActualDeparture is { } actualDeparture)
        {
            var departureErrors = RecordVoyageArrivalValidator.ValidateAgainstActualDeparture(
                actualArrivalUtc,
                actualDeparture);

            if (departureErrors.Count > 0)
            {
                throw new RecordVoyageArrivalValidationException(departureErrors);
            }
        }

        voyage.Arrive(actualArrivalUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordVoyageArrivalResponse(
            voyage.Id,
            actualArrivalUtc,
            voyage.Status);
    }
}
