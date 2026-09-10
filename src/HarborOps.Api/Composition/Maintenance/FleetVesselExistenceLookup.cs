using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Api.Composition.Maintenance;

public sealed class FleetVesselExistenceLookup(IVesselLookupService lookupService)
    : IVesselExistenceLookup
{
    public async Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        var response = await lookupService.LookupAsync(
            new VesselLookupRequest { VesselIds = [vesselId] },
            cancellationToken);

        var vessel = response.Vessels.SingleOrDefault();

        return vessel is null ? null : new VesselExistenceResult(vessel.Id, vessel.Name);
    }
}
