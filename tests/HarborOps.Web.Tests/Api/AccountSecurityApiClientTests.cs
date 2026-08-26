using System.Net;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class AccountSecurityApiClientTests
{
    [Fact]
    public async Task Change_password_posts_only_the_current_and_new_passwords()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await new AccountSecurityApiClient(client)
            .ChangePasswordAsync("CurrentP@ssw0rd!", "NewP@ssw0rd!", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("/api/identity/account/change-password", capturedRequest.RequestUri?.AbsolutePath);
        var body = await capturedRequest.Content!.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("currentPassword", body, StringComparison.Ordinal);
        Assert.Contains("newPassword", body, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmPassword", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_sessions_returns_unauthorized_result_without_treating_it_as_success()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await new AccountSecurityApiClient(client).RevokeSessionsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.IsUnauthorized);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        new(new StubHttpMessageHandler((request, _) => Task.FromResult(responseFactory(request))))
        {
            BaseAddress = new Uri("https://web.harborops.test/")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
