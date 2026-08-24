namespace HarborOps.Modules.Voyages.Contracts;

public interface ISearchBerthsQuery
{
    Task<SearchBerthsResponse> SearchAsync(
        SearchBerthsRequest request,
        CancellationToken cancellationToken);
}
