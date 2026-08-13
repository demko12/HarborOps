using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class CrewApiClientTests
{
    [Fact]
    public async Task GetMembersAsync_sends_search_status_and_paging_to_crew_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"items":[],"pageNumber":2,"pageSize":10,"totalCount":0}""");
        });

        var result = await new CrewApiClient(httpClient).GetMembersAsync("Ana Novak", "Active", 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/crew/members", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("search=Ana%20Novak", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("employmentStatus=Active", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", captured.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMembersAsync_returns_retryable_failure_when_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new CrewApiClient(httpClient).GetMembersAsync(null, null, 1, 25, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
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
