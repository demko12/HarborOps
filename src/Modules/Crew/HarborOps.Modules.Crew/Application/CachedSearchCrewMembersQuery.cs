using HarborOps.Modules.Crew.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace HarborOps.Modules.Crew.Application;

internal sealed class CachedSearchCrewMembersQuery(
    SearchCrewMembersQuery inner,
    IMemoryCache cache) : ISearchCrewMembersQuery
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(20);

    public async Task<SearchCrewMembersResponse> SearchAsync(
        SearchCrewMembersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cacheKey = string.Join(
            '|',
            "crew-search",
            request.Search?.Trim().ToUpperInvariant() ?? string.Empty,
            request.EmploymentStatus?.Trim() ?? string.Empty,
            request.PassportExpiry?.Trim() ?? string.Empty,
            request.PageNumber?.ToString() ?? string.Empty,
            request.PageSize?.ToString() ?? string.Empty);

        if (cache.TryGetValue(cacheKey, out SearchCrewMembersResponse? cached) && cached is not null)
        {
            return cached;
        }

        var response = await inner.SearchAsync(request, cancellationToken);
        cache.Set(cacheKey, response, CacheDuration);

        return response;
    }

    public Task<CrewMembersSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken) =>
        inner.GetSummaryAsync(cancellationToken);
}
