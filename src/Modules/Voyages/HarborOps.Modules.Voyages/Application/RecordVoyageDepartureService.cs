using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RecordVoyageDepartureService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<RecordVoyageDepartureResponse> RecordDepartureAsync(
        Guid voyageId,
        RecordVoyageDepartureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = RecordVoyageDepartureValidator.Validate(request, utcNow);
        if (errors.Count > 0)
        {
            throw new RecordVoyageDepartureValidationException(errors);
        }

        var actualDepartureUtc = request.ActualDepartureUtc!.Value;

        var voyage = await dbContext
            .Set<Voyage>()
            .FirstOrDefaultAsync(candidate => candidate.Id == voyageId, cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        var createdUtcErrors = RecordVoyageDepartureValidator.ValidateAgainstCreatedUtc(
            actualDepartureUtc,
            voyage.CreatedUtc);

        if (createdUtcErrors.Count > 0)
        {
            throw new RecordVoyageDepartureValidationException(createdUtcErrors);
        }

        voyage.Depart(actualDepartureUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordVoyageDepartureResponse(
            voyage.Id,
            actualDepartureUtc,
            voyage.Status);
    }
}
