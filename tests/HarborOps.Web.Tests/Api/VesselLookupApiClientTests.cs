using System.Net;
using System.Text;
using HarborOps.Web.Api.Fleet;

namespace HarborOps.Web.Tests.Api;

public sealed class VesselLookupApiClientTests
{
    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_id_to_name_map()
    {
        var vesselId = Guid.NewGuid();
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.OK,
            $$"""{"vessels":[{"id":"{{vesselId}}","name":"Aurora"}]}"""));

        var result = await new VesselLookupApiClient(httpClient)
            .LookupVesselNamesAsync([vesselId], CancellationToken.None);

        Assert.Equal("Aurora", result[vesselId]);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_empty_map_without_calling_the_api_for_no_ids()
    {
        var called = false;
        using var httpClient = CreateHttpClient((_, _) =>
        {
            called = true;
            return Json(HttpStatusCode.OK, """{"vessels":[]}""");
        });

        var result = await new VesselLookupApiClient(httpClient)
            .LookupVesselNamesAsync([], CancellationToken.None);

        Assert.Empty(result);
        Assert.False(called);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_empty_map_when_the_api_is_unreachable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("Connection failed."));

        var result = await new VesselLookupApiClient(httpClient)
            .LookupVesselNamesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LookupVesselNamesAsync_returns_an_empty_map_for_a_non_success_response()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.BadRequest, "{}"));

        var result = await new VesselLookupApiClient(httpClient)
            .LookupVesselNamesAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.Empty(result);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
