using System.Net;
using System.Text;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class IdentitySessionApiClientTests
{
    [Fact]
    public async Task LoginAsync_posts_credentials_only_to_identity_login_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var client = CreateClient(async (request, _) =>
        {
            captured = request;
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("captain@harborops.test", body, StringComparison.Ordinal);
            return await Json(HttpStatusCode.OK, """{"accessToken":"access","refreshToken":"refresh","expiresIn":900,"tokenType":"Bearer"}""");
        });

        var result = await new IdentitySessionApiClient(client).LoginAsync("captain@harborops.test", "P@ssw0rd!", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("access", result.AccessToken);
        Assert.Equal("/api/identity/login", captured!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task LoginAsync_returns_null_for_invalid_credentials()
    {
        using var client = CreateClient((_, _) => Json(HttpStatusCode.Unauthorized, ""));

        var result = await new IdentitySessionApiClient(client).LoginAsync("captain@harborops.test", "bad", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_returns_null_when_refresh_is_expired()
    {
        using var client = CreateClient((_, _) => Json(HttpStatusCode.Unauthorized, ""));

        var result = await new IdentitySessionApiClient(client).RefreshAsync("expired", CancellationToken.None);

        Assert.Null(result);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send)) { BaseAddress = new Uri("https://api.harborops.test/") };

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
