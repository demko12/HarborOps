namespace HarborOps.Modules.Crew.Contracts;

public interface ISearchCrewMembersQuery
{
    Task<SearchCrewMembersResponse> SearchAsync(SearchCrewMembersRequest request, CancellationToken cancellationToken);
}
