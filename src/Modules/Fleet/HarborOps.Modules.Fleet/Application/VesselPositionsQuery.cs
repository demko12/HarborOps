using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class VesselPositionsQuery(
    DbContext dbContext,
    TimeProvider timeProvider,
    AisTrackingState? aisTracking = null)
    : IVesselPositionsQuery
{
    internal const double StaleAfterHours = 12;

    public async Task<VesselPositionsResponse> GetAsync(
        VesselPositionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = VesselPositionsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new VesselPositionsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? VesselPositionsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? VesselPositionsValidator.DefaultPageSize;

        var evaluatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var staleBeforeUtc = evaluatedAtUtc.AddHours(-StaleAfterHours);

        var fleetAssignments = dbContext.Set<FleetAssignment>().AsNoTracking();
        var fleets = dbContext.Set<Domain.Fleet>().AsNoTracking();

        var rows = dbContext.Set<Vessel>()
            .AsNoTracking()
            .Select(vessel => new
            {
                Vessel = vessel,
                IsPositionStale =
                    vessel.CurrentLatitude == null
                    || vessel.CurrentLongitude == null
                    || vessel.LastPositionReportedAtUtc == null
                    || vessel.LastPositionReportedAtUtc < staleBeforeUtc,
            });

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            rows = rows.Where(row => row.Vessel.Name.Contains(search) || row.Vessel.ImoNumber.Contains(search)
                || (row.Vessel.Mmsi != null && row.Vessel.Mmsi.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            rows = rows.Where(row => row.Vessel.CurrentStatus == status);
        }

        if (request.FleetId is { } fleetId)
        {
            rows = rows.Where(row => fleetAssignments.Any(assignment =>
                assignment.VesselId == row.Vessel.Id
                && assignment.IsActive
                && assignment.FleetId == fleetId));
        }

        if (request.StaleOnly == true)
        {
            rows = rows.Where(row => row.IsPositionStale);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            // Operators need to see the report they have just accepted without paging past
            // older exceptions. The attention panel separately keeps stale/missing vessels visible.
            .OrderByDescending(row => row.Vessel.LastPositionReportedAtUtc)
            .ThenBy(row => row.Vessel.Name)
            .ThenBy(row => row.Vessel.ImoNumber)
            .ThenBy(row => row.Vessel.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new VesselPositionListItem(
                row.Vessel.Id,
                row.Vessel.Name,
                row.Vessel.ImoNumber,
                row.Vessel.CurrentStatus,
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
                 select fleet.Name).FirstOrDefault(),
                row.Vessel.CurrentLatitude,
                row.Vessel.CurrentLongitude,
                row.Vessel.LastPositionReportedAtUtc,
                row.IsPositionStale,
                null,
                row.Vessel.Mmsi))
            .ToListAsync(cancellationToken);

        return new VesselPositionsResponse(
            items,
            pageNumber,
            pageSize,
            totalCount,
            StaleAfterHours,
            evaluatedAtUtc,
            aisTracking?.Status);
    }
}
