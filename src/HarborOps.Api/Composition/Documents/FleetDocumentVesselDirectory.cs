using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.Api.Composition.Documents;

public sealed class FleetDocumentVesselDirectory(ISearchVesselsQuery searchVesselsQuery)
    : IVesselDirectory
{
    public async Task<VesselDirectoryPage> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var response = await searchVesselsQuery.SearchAsync(
            new SearchVesselsRequest { PageNumber = pageNumber, PageSize = pageSize },
            cancellationToken);

        return new VesselDirectoryPage(
            response.Items.Select(vessel => new VesselExistenceResult(vessel.VesselId, vessel.Name)).ToList(),
            response.TotalCount);
    }
}
