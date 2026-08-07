using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.UnitTests.Operations;

internal sealed class FakeSearchVesselsQuery(SearchVesselsResponse response) : ISearchVesselsQuery
{
    internal int CallCount { get; private set; }

    internal SearchVesselsRequest? LastRequest { get; private set; }

    public Task<SearchVesselsResponse> SearchAsync(
        SearchVesselsRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        return Task.FromResult(response);
    }
}
