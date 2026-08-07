namespace HarborOps.Modules.Fleet.Contracts;

public interface ISearchVesselsQuery
{
    Task<SearchVesselsResponse> SearchAsync(
        SearchVesselsRequest request,
        CancellationToken cancellationToken);
}
