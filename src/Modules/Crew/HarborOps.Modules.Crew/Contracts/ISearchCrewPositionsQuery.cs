namespace HarborOps.Modules.Crew.Contracts;

public interface ISearchCrewPositionsQuery
{
    Task<SearchCrewPositionsResponse> SearchAsync(SearchCrewPositionsRequest request, CancellationToken cancellationToken);
}
