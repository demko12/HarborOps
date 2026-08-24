using System.Net;
using System.Text;
using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Tests.Api;

public sealed class PortApiClientTests
{
    private static readonly Guid RotterdamId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid HamburgId = Guid.Parse("50000000-0000-0000-0000-000000000005");

    private const string PageJson = """
        {"items":[{"portId":"40000000-0000-0000-0000-000000000004","name":"Rotterdam",
        "country":"Netherlands","city":"Rotterdam","unLocode":"NLRTM"}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string NamesJson = """
        {"ports":[{"portId":"40000000-0000-0000-0000-000000000004","name":"Rotterdam","country":"Netherlands"},
        {"portId":"50000000-0000-0000-0000-000000000005","name":"Hamburg","country":"Germany"}]}
        """;

    [Fact]
    public async Task GetPortsAsync_sends_the_search_and_the_page()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(HttpStatusCode.OK, PageJson);
        });

        var result = await CreateClient(httpClient).GetPortsAsync(
            "Rotter",
            null,
            pageNumber: 1,
            pageSize: 25,
            CancellationToken.None);

        var path = captured!.RequestUri!.PathAndQuery;

        Assert.True(result.IsSuccess);
        Assert.StartsWith("/api/voyages/ports?", path, StringComparison.Ordinal);
        Assert.Contains("search=Rotter", path, StringComparison.Ordinal);
        Assert.Contains("pageSize=25", path, StringComparison.Ordinal);
        Assert.Equal(RotterdamId, Assert.Single(result.Value!.Items).PortId);
    }

    [Fact]
    public async Task LookupPortNamesAsync_requests_the_identifiers_and_maps_the_names()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(HttpStatusCode.OK, NamesJson);
        });

        var names = await CreateClient(httpClient).LookupPortNamesAsync(
            [RotterdamId, HamburgId],
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/api/voyages/ports/names", captured.RequestUri!.AbsolutePath);
        Assert.Contains(RotterdamId.ToString(), captured.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(HamburgId.ToString(), captured.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Rotterdam", names[RotterdamId]);
        Assert.Equal("Hamburg", names[HamburgId]);
    }

    [Fact]
    public async Task LookupPortNamesAsync_skips_the_call_for_an_empty_request()
    {
        var called = false;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            called = true;

            return Json(HttpStatusCode.OK, NamesJson);
        });

        var names = await CreateClient(httpClient).LookupPortNamesAsync([], CancellationToken.None);

        Assert.False(called);
        Assert.Empty(names);
    }

    [Fact]
    public async Task LookupPortNamesAsync_leaves_a_board_nameless_rather_than_failing()
    {
        using var unreachable = CreateHttpClient((_, _) => throw new HttpRequestException("boom"));
        using var forbidden = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, "{}"));

        var whenUnreachable = await CreateClient(unreachable).LookupPortNamesAsync(
            [RotterdamId],
            CancellationToken.None);
        var whenForbidden = await CreateClient(forbidden).LookupPortNamesAsync(
            [RotterdamId],
            CancellationToken.None);

        Assert.Empty(whenUnreachable);
        Assert.Empty(whenForbidden);
    }

    private static VoyagesApiClient CreateClient(HttpClient httpClient) => new(httpClient);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
