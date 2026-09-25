namespace HarborOps.Modules.Documents.Contracts;

public interface IVesselDirectory
{
    Task<VesselDirectoryPage> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);
}
