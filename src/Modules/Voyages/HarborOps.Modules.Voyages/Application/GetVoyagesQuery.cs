using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetVoyagesQuery(DbContext dbContext) : IGetVoyagesQuery
{
    public async Task<GetVoyagesResponse> GetVoyagesAsync(
        GetVoyagesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetVoyagesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetVoyagesValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetVoyagesValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetVoyagesValidator.DefaultPageSize;

        var windowStart = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var windowEndExclusive = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var voyages = dbContext.Set<Voyage>()
            .AsNoTracking();

        voyages = request.IncludeActive == true
            ? voyages.Where(voyage =>
                (voyage.PlannedDeparture >= windowStart && voyage.PlannedDeparture < windowEndExclusive)
                || voyage.Status == Voyage.InProgressStatus
                || (voyage.Status == Voyage.PlannedStatus && voyage.PlannedDeparture < windowStart))
            : voyages.Where(voyage => voyage.PlannedDeparture >= windowStart
                && voyage.PlannedDeparture < windowEndExclusive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            voyages = voyages.Where(voyage => voyage.VoyageNumber.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status;
            voyages = voyages.Where(voyage => voyage.Status == status);
        }

        var totalCount = await voyages.CountAsync(cancellationToken);

        var items = await voyages
            .OrderBy(voyage => voyage.PlannedDeparture)
            .ThenBy(voyage => voyage.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(voyage => new VoyageListItem(
                voyage.Id,
                voyage.VoyageNumber,
                voyage.VesselId,
                voyage.DeparturePortCode,
                voyage.ArrivalPortCode,
                voyage.Status,
                voyage.PlannedDeparture,
                voyage.PlannedArrival,
                voyage.ActualDeparture,
                voyage.ActualArrival))
            .ToListAsync(cancellationToken);

        return new GetVoyagesResponse(items, pageNumber, pageSize, totalCount);
    }
}
