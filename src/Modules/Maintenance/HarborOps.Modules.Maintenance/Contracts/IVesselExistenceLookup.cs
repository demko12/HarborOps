namespace HarborOps.Modules.Maintenance.Contracts;

/// <summary>Maintenance's own port for vessel existence lookup, backed by an Api-composition-root adapter over Fleet's public contract.</summary>
public interface IVesselExistenceLookup
{
    Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken);
}
