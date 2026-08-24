namespace HarborOps.Modules.Voyages.Contracts;

public interface ISearchPortsQuery
{
    Task<SearchPortsResponse> SearchAsync(
        SearchPortsRequest request,
        CancellationToken cancellationToken);
}
