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

        var fleetAssignments = dbContext.Set<FleetAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.IsActive);
        var fleets = dbContext.Set<Domain.Fleet>().AsNoTracking();

        var vessels = dbContext.Set<Vessel>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();

            vessels = vessels.Where(vessel =>
                vessel.Name.Contains(search)
                || vessel.ImoNumber == search);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            vessels = vessels.Where(vessel => vessel.CurrentStatus == status);
        }

        if (request.FleetId is { } fleetId)
        {
            vessels = vessels.Where(vessel => fleetAssignments.Any(assignment =>
                assignment.VesselId == vessel.Id
                && assignment.FleetId == fleetId));
        }

        var totalCount = await vessels.CountAsync(cancellationToken);

        var rows =
            from vessel in vessels
            join vesselType in dbContext.Set<VesselType>().AsNoTracking()
                on vessel.VesselTypeId equals vesselType.Id
            join fleetAssignment in fleetAssignments
                on vessel.Id equals fleetAssignment.VesselId into vesselAssignments
            from fleetAssignment in vesselAssignments
                .OrderBy(assignment => assignment.AssignedFromUtc)
                .ThenBy(assignment => assignment.Id)
                .Take(1)
                .DefaultIfEmpty()
            join fleet in fleets
                on fleetAssignment.FleetId equals fleet.Id into assignedFleets
            from fleet in assignedFleets.DefaultIfEmpty()
            select new
            {
                Vessel = vessel,
                VesselTypeName = vesselType.Name,
                FleetId = (Guid?)fleetAssignment.FleetId,
                FleetName = fleet.Name,
            };

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
                row.Vessel.VesselTypeId,
                row.VesselTypeName,
                row.Vessel.CompanyId,
                row.Vessel.Dwt,
                row.Vessel.UpdatedAtUtc,
                row.Vessel.LifecycleStage,
                row.FleetId,
                row.FleetName,
                row.Vessel.CurrentLatitude,
                row.Vessel.CurrentLongitude,
                row.Vessel.LastPositionReportedAtUtc))
            .ToListAsync(cancellationToken);

        return new SearchVesselsResponse(items, pageNumber, pageSize, totalCount);
    }
}
