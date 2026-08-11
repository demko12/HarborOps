using System.Net;
using HarborOps.Web.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarborOps.Web.Tests.Api;

public sealed class HealthApiClientTests
{
    [Fact]
    public async Task IsAvailableAsync_returns_true_and_uses_configured_base_address()
    {
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = CreateClient(httpClient);

        var result = await client.IsAvailableAsync(CancellationToken.None);

        Assert.True(result);
        Assert.Equal(new Uri("https://api.harborops.test/health"), requestedUri);
    }

    [Fact]
    public async Task IsAvailableAsync_returns_false_for_unsuccessful_response()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = CreateClient(httpClient);

        var result = await client.IsAvailableAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsAvailableAsync_returns_false_when_api_cannot_be_reached()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            throw new HttpRequestException("Connection failed."));
        var client = CreateClient(httpClient);

        var result = await client.IsAvailableAsync(CancellationToken.None);

        Assert.False(result);
    }

    private static HealthApiClient CreateClient(HttpClient httpClient)
    {
        return new HealthApiClient(httpClient, NullLogger<HealthApiClient>.Instance);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        return new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }
}
