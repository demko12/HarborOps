using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class FleetApiClientTests
{
    [Fact]
    public async Task GetVesselsAsync_sends_search_status_and_paging_to_fleet_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(
            "Northern Star", "Active", 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/fleet/vessels", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Northern%20Star", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("status=Active", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVesselsAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    [Fact]
    public async Task GetVesselsAsync_returns_clear_message_when_fleet_access_is_forbidden()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Fleet access denied", result.Failure!.Title);
    }

    [Fact]
    public async Task GetVesselsAsync_surfaces_validation_errors_from_problem_details()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"status":["Status is not a known vessel status."]}}"""));

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(
            null, "Sailing", 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal("One or more validation errors occurred.", result.Failure.Title);
        Assert.Equal(
            "Status is not a known vessel status.",
            Assert.Single(result.Failure.Errors["status"]));
    }

    [Fact]
    public async Task GetVesselsAsync_reports_no_validation_errors_for_other_failures()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new FleetApiClient(httpClient).GetVesselsAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.Failure!.IsValidation);
        Assert.Empty(result.Failure.Errors);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
