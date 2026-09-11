using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

internal sealed class FakeVesselExistenceLookup(VesselExistenceResult? result = null) : IVesselExistenceLookup
{
    internal int CallCount { get; private set; }

    internal Guid? LastVesselId { get; private set; }

    public Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        CallCount++;
        LastVesselId = vesselId;

        return Task.FromResult(result);
    }
}
