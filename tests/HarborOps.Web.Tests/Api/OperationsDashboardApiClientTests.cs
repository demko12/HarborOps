using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class OperationsDashboardApiClientTests
{
    [Fact]
    public async Task GetAsync_sends_the_captured_as_of_date_and_reads_sections()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """
                {"asOf":"2026-08-17","sections":[{"capability":"fleet","title":"Fleet","summaryLabel":"Active vessels","count":4,"link":"/fleet?status=Active","items":[],"isUnavailable":false}]}
                """);
        });

        var result = await new OperationsDashboardApiClient(httpClient).GetAsync(
            new DateOnly(2026, 8, 17),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/operations/dashboard", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("asOf=2026-08-17", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("fleet", Assert.Single(result.Value!.Sections).Capability);
    }

    [Fact]
    public async Task GetAsync_returns_a_safe_forbidden_failure()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.Forbidden, string.Empty));

        var result = await new OperationsDashboardApiClient(httpClient).GetAsync(
            new DateOnly(2026, 8, 17),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsForbidden);
        Assert.Equal("Dashboard access denied", result.Failure.Title);
        Assert.DoesNotContain("exception", result.Failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_returns_a_retryable_failure_when_the_api_is_unavailable()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException());

        var result = await new OperationsDashboardApiClient(httpClient).GetAsync(
            new DateOnly(2026, 8, 17),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.Failure!.StatusCode);
        Assert.Equal("HarborOps API is unavailable", result.Failure.Title);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
