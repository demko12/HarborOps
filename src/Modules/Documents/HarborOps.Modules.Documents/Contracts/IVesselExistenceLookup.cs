namespace HarborOps.Modules.Documents.Contracts;

public interface IVesselExistenceLookup
{
    Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken);
}
