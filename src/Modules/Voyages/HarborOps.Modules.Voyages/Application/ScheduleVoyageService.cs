using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class ScheduleVoyageService(DbContext dbContext)
{
    public async Task<ScheduleVoyageResponse> ScheduleAsync(
        ScheduleVoyageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ScheduleVoyageValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ScheduleVoyageValidationException(errors);
        }

        var voyageNumber = request.VoyageNumber!.Trim();
        var vesselId = request.VesselId!.Value;
        var departurePortCode = ScheduleVoyageValidator.NormalizePortCode(
            request.DeparturePortCode!);
        var arrivalPortCode = ScheduleVoyageValidator.NormalizePortCode(
            request.ArrivalPortCode!);
        var voyages = dbContext.Set<Voyage>();

        var voyageNumberExists = await voyages
            .AsNoTracking()
            .AnyAsync(voyage => voyage.VoyageNumber == voyageNumber, cancellationToken);

        if (voyageNumberExists)
        {
            throw new DuplicateVoyageNumberException(voyageNumber);
        }

        var voyage = Voyage.Schedule(
            vesselId,
            voyageNumber,
            departurePortCode,
            arrivalPortCode,
            request.PlannedDepartureUtc!.Value,
            request.PlannedArrivalUtc!.Value);

        await voyages.AddAsync(voyage, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ScheduleVoyageResponse(voyage.Id);
    }
}
