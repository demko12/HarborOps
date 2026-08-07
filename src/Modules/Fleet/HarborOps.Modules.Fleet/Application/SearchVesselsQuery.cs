using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class SearchVesselsQuery(DbContext dbContext) : ISearchVesselsQuery
{
    public async Task<SearchVesselsResponse> SearchAsync(
        SearchVesselsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = SearchVesselsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new SearchVesselsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? SearchVesselsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? SearchVesselsValidator.DefaultPageSize;

        var fleetAssignments = dbContext.Set<FleetAssignment>().AsNoTracking();
        var fleets = dbContext.Set<Domain.Fleet>().AsNoTracking();

        var rows =
            from vessel in dbContext.Set<Vessel>().AsNoTracking()
            join vesselType in dbContext.Set<VesselType>().AsNoTracking()
                on vessel.VesselTypeId equals vesselType.Id
            select new
            {
                Vessel = vessel,
                VesselTypeName = vesselType.Name,
            };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();

            rows = rows.Where(row =>
                row.Vessel.Name.Contains(search)
                || row.Vessel.ImoNumber.Contains(search));
        }

        if (request.FleetId is { } fleetId)
        {
            rows = rows.Where(row => fleetAssignments.Any(assignment =>
                assignment.VesselId == row.Vessel.Id
                && assignment.IsActive
                && assignment.FleetId == fleetId));
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(row => row.Vessel.Name)
            .ThenBy(row => row.Vessel.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new VesselListItem(
                row.Vessel.Id,
                row.Vessel.Name,
                row.Vessel.ImoNumber,
                row.Vessel.FlagState,
                row.Vessel.CurrentStatus,
                row.Vessel.LifecycleStage,
                row.Vessel.VesselTypeId,
                row.VesselTypeName,
                fleetAssignments
                    .Where(assignment =>
                        assignment.VesselId == row.Vessel.Id && assignment.IsActive)
                    .OrderBy(assignment => assignment.AssignedFromUtc)
                    .ThenBy(assignment => assignment.Id)
                    .Select(assignment => (Guid?)assignment.FleetId)
                    .FirstOrDefault(),
                (from assignment in fleetAssignments
                 where assignment.VesselId == row.Vessel.Id && assignment.IsActive
                 join fleet in fleets on assignment.FleetId equals fleet.Id
                 orderby assignment.AssignedFromUtc, assignment.Id
                 select fleet.Name).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new SearchVesselsResponse(items, pageNumber, pageSize, totalCount);
    }
}
