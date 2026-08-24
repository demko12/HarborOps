using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class FleetManagementQuery(DbContext dbContext) : IFleetManagementQuery
{
    public async Task<SearchFleetsResponse> SearchAsync(SearchFleetsRequest request, CancellationToken cancellationToken)
    {
        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100)
        {
            throw new ArgumentException("Paging values are not valid.");
        }

        var search = request.Search?.Trim();
        var fleets = dbContext.Set<Domain.Fleet>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            fleets = fleets.Where(fleet => fleet.Name.Contains(search) || fleet.Description.Contains(search));
        }

        var totalCount = await fleets.CountAsync(cancellationToken);
        var assignments = dbContext.Set<FleetAssignment>().AsNoTracking();
        var items = await fleets
            .OrderBy(fleet => fleet.Name).ThenBy(fleet => fleet.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(fleet => new FleetListItem(
                fleet.Id, fleet.Name, fleet.Description, fleet.CreatedAtUtc,
                assignments.Count(assignment => assignment.FleetId == fleet.Id && assignment.IsActive)))
            .ToArrayAsync(cancellationToken);
        return new SearchFleetsResponse(items, totalCount, request.PageNumber, request.PageSize);
    }

    public async Task<FleetDetailsResponse?> GetDetailsAsync(Guid fleetId, CancellationToken cancellationToken)
    {
        var fleet = await dbContext.Set<Domain.Fleet>().AsNoTracking()
            .Where(item => item.Id == fleetId)
            .Select(item => new { item.Id, item.Name, item.Description, item.CreatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (fleet is null) return null;
        var vessels = dbContext.Set<Vessel>().AsNoTracking();
        var assignments = dbContext.Set<FleetAssignment>().AsNoTracking();
        var activeVessels = await assignments.Where(item => item.FleetId == fleetId && item.IsActive)
            .Join(vessels, assignment => assignment.VesselId, vessel => vessel.Id, (assignment, vessel) => new { Assignment = assignment, Vessel = vessel })
            .OrderBy(item => item.Vessel.Name).ThenBy(item => item.Vessel.Id)
            .Select(item => new FleetVesselItem(item.Vessel.Id, item.Vessel.Name, item.Vessel.ImoNumber, item.Vessel.CurrentStatus.ToString(), item.Assignment.AssignedFromUtc))
            .ToArrayAsync(cancellationToken);
        return new FleetDetailsResponse(fleet.Id, fleet.Name, fleet.Description, fleet.CreatedAtUtc, activeVessels);
    }

    public async Task<FleetAssignmentHistoryResponse?> GetAssignmentHistoryAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Set<Vessel>().AsNoTracking().AnyAsync(vessel => vessel.Id == vesselId, cancellationToken)) return null;
        var items = await dbContext.Set<FleetAssignment>().AsNoTracking()
            .Where(assignment => assignment.VesselId == vesselId)
            .Join(dbContext.Set<Domain.Fleet>().AsNoTracking(), assignment => assignment.FleetId, fleet => fleet.Id,
                (assignment, fleet) => new { AssignmentId = assignment.Id, FleetId = fleet.Id, fleet.Name, assignment.AssignedFromUtc, assignment.AssignedToUtc, assignment.IsActive })
            .OrderByDescending(item => item.AssignedFromUtc).ThenByDescending(item => item.AssignmentId)
            .Select(item => new FleetAssignmentHistoryItem(item.FleetId, item.Name, item.AssignedFromUtc, item.AssignedToUtc, item.IsActive))
            .ToArrayAsync(cancellationToken);
        return new FleetAssignmentHistoryResponse(items);
    }
}
