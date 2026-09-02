using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class GetVesselDetailsQuery(DbContext dbContext) : IGetVesselDetailsQuery
{
    public async Task<VesselDetailsResponse?> GetAsync(
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        var fleetAssignments = dbContext.Set<FleetAssignment>().AsNoTracking();
        var fleets = dbContext.Set<Domain.Fleet>().AsNoTracking();

        var vessel = await (
            from v in dbContext.Set<Vessel>().AsNoTracking()
            join vesselType in dbContext.Set<VesselType>().AsNoTracking()
                on v.VesselTypeId equals vesselType.Id
            where v.Id == vesselId
            select new
            {
                v.Id,
                v.CompanyId,
                v.Name,
                v.ImoNumber,
                v.Mmsi,
                v.CallSign,
                v.FlagState,
                v.VesselTypeId,
                VesselTypeName = vesselType.Name,
                v.Dwt,
                v.Gt,
                v.Loa,
                v.Beam,
                v.Draft,
                v.PortOfRegistry,
                v.ClassificationSociety,
                v.LifecycleStage,
                v.MsmdId,
                v.CurrentStatus,
                v.CreatedAtUtc,
                v.UpdatedAtUtc,
                FleetId = fleetAssignments
                    .Where(assignment => assignment.VesselId == v.Id && assignment.IsActive)
                    .OrderBy(assignment => assignment.AssignedFromUtc)
                    .ThenBy(assignment => assignment.Id)
                    .Select(assignment => (Guid?)assignment.FleetId)
                    .FirstOrDefault(),
                FleetName = (from assignment in fleetAssignments
                             where assignment.VesselId == v.Id && assignment.IsActive
                             join fleet in fleets on assignment.FleetId equals fleet.Id
                             orderby assignment.AssignedFromUtc, assignment.Id
                             select fleet.Name).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (vessel is null)
        {
            return null;
        }

        var history = await dbContext.Set<VesselStatusHistory>()
            .AsNoTracking()
            .Where(entry => entry.VesselId == vesselId)
            .OrderByDescending(entry => entry.ChangedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new VesselStatusHistoryItem(
                entry.Id,
                entry.Status,
                entry.ChangedBy,
                entry.Notes,
                entry.ChangedAtUtc))
            .ToListAsync(cancellationToken);

        return new VesselDetailsResponse(
            vessel.Id,
            vessel.CompanyId,
            vessel.Name,
            vessel.ImoNumber,
            vessel.Mmsi,
            vessel.CallSign,
            vessel.FlagState,
            vessel.VesselTypeId,
            vessel.VesselTypeName,
            vessel.Dwt,
            vessel.Gt,
            vessel.Loa,
            vessel.Beam,
            vessel.Draft,
            vessel.PortOfRegistry,
            vessel.ClassificationSociety,
            vessel.LifecycleStage,
            vessel.MsmdId,
            vessel.CurrentStatus,
            vessel.CreatedAtUtc,
            vessel.UpdatedAtUtc,
            vessel.FleetId,
            vessel.FleetName,
            history);
    }
}
