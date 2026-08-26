using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;

namespace HarborOps.IntegrationTests.Identity;

public sealed class AccountSecurityEndpointTests : IntegrationTestBase
{
    private const string CurrentPassword = "P@ssw0rd!2026";
    private const string NewPassword = "NewP@ssw0rd!2026";

    private readonly HarborOpsApiFactory _application;

    public AccountSecurityEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Theory]
    [InlineData("/api/identity/account/change-password")]
    [InlineData("/api/identity/account/revoke-sessions")]
    public async Task Account_security_endpoints_return_401_when_anonymous(string route)
    {
        using var client = _application.CreateClient();
        using var response = route.EndsWith("change-password", StringComparison.Ordinal)
            ? await client.PostAsJsonAsync(route, new ChangePasswordRequest(CurrentPassword, NewPassword), CancellationToken.None)
            : await client.PostAsync(route, content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_returns_400_for_an_incorrect_current_password_without_changing_credentials()
    {
        var email = UniqueEmail();
        await _application.CreateTestUserAsync(email, CurrentPassword);
        using var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, email, CurrentPassword));

        using var response = await client.PostAsJsonAsync(
            "/api/identity/account/change-password",
            new ChangePasswordRequest("WrongP@ssw0rd!2026", NewPassword),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var unchangedPasswordResponse = await LoginResponseAsync(client, email, CurrentPassword);
        Assert.Equal(HttpStatusCode.OK, unchangedPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task Change_password_invalidates_existing_access_and_refresh_tokens()
    {
        var email = UniqueEmail();
        await _application.CreateTestUserAsync(email, CurrentPassword);
        using var client = _application.CreateClient();
        var tokens = await LoginTokensAsync(client, email, CurrentPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var changeResponse = await client.PostAsJsonAsync(
            "/api/identity/account/change-password",
            new ChangePasswordRequest(CurrentPassword, NewPassword),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        using var revokedAccessResponse = await client.GetAsync("/api/identity/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccessResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        using var revokedRefreshResponse = await client.PostAsJsonAsync(
            "/api/identity/refresh",
            new { refreshToken = tokens.RefreshToken },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefreshResponse.StatusCode);

        using var oldPasswordResponse = await LoginResponseAsync(client, email, CurrentPassword);
        using var newPasswordResponse = await LoginResponseAsync(client, email, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task Revoke_sessions_invalidates_only_the_current_users_sessions()
    {
        var currentUserEmail = UniqueEmail();
        var otherUserEmail = UniqueEmail();
        await _application.CreateTestUserAsync(currentUserEmail, CurrentPassword);
        await _application.CreateTestUserAsync(otherUserEmail, CurrentPassword);

        using var currentUserClient = _application.CreateClient();
        using var otherUserClient = _application.CreateClient();
        currentUserClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(currentUserClient, currentUserEmail, CurrentPassword));
        otherUserClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(otherUserClient, otherUserEmail, CurrentPassword));

        using var revokeResponse = await currentUserClient.PostAsync(
            "/api/identity/account/revoke-sessions",
            content: null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var currentUserResponse = await currentUserClient.GetAsync("/api/identity/me", CancellationToken.None);
        using var otherUserResponse = await otherUserClient.GetAsync("/api/identity/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, currentUserResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherUserResponse.StatusCode);
    }

    private static string UniqueEmail() => $"account.security.{Guid.NewGuid():N}@harborops.test";

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var tokens = await LoginTokensAsync(client, email, password);
        return tokens.AccessToken;
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginTokensAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var response = await LoginResponseAsync(client, email, password);
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return (tokens.GetProperty("accessToken").GetString()!, tokens.GetProperty("refreshToken").GetString()!);
    }

    private static Task<HttpResponseMessage> LoginResponseAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/identity/login", new { email, password }, CancellationToken.None);
}
