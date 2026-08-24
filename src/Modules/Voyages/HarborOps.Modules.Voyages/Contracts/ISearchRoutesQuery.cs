namespace HarborOps.Modules.Voyages.Contracts;

public interface ISearchRoutesQuery
{
    Task<SearchRoutesResponse> SearchAsync(
        SearchRoutesRequest request,
        CancellationToken cancellationToken);
}
