using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.IntegrationTests.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Identity;

public sealed class IdentityEndpointsTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public IdentityEndpointsTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Register_is_unavailable_for_anonymous_and_authenticated_callers_and_does_not_persist_an_account()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();

        using var anonymousResponse = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password = ValidPassword },
            CancellationToken.None);
        using var authenticatedClient = await _application.CreateAuthenticatedClientAsync();
        using var authenticatedResponse = await authenticatedClient.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password = ValidPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, authenticatedResponse.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    [Fact]
    public async Task Unsupported_account_recovery_endpoints_are_unavailable()
    {
        using var client = _application.CreateClient();
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "/api/identity/confirmEmail"),
            new HttpRequestMessage(HttpMethod.Post, "/api/identity/resendConfirmationEmail"),
            new HttpRequestMessage(HttpMethod.Post, "/api/identity/forgotPassword"),
            new HttpRequestMessage(HttpMethod.Post, "/api/identity/resetPassword"),
        };

        foreach (var request in requests)
        {
            using (request)
            using (var response = await client.SendAsync(request, CancellationToken.None))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_usable_access_and_refresh_tokens()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();
        await SeedUserAsync(email, ValidPassword);

        using var response = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = ValidPassword },
            CancellationToken.None);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("refreshToken").GetString()));

        // The access token must actually be usable against a protected endpoint.
        using var meResponse = await GetMeAsync(client, tokens.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Login_with_invalid_credentials_returns_401_without_disclosing_account_existence()
    {
        using var client = _application.CreateClient();
        var registeredEmail = UniqueEmail();
        await SeedUserAsync(registeredEmail, ValidPassword);

        using var wrongPasswordResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email = registeredEmail, password = "WrongPassword!123" },
            CancellationToken.None);

        using var unknownEmailResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email = UniqueEmail(), password = ValidPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmailResponse.StatusCode);

        // Both failure modes must be indistinguishable to the caller.
        Assert.Equal(wrongPasswordResponse.StatusCode, unknownEmailResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_valid_refresh_token_succeeds()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();
        await SeedUserAsync(email, ValidPassword);

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = ValidPassword },
            CancellationToken.None);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var refreshToken = tokens.GetProperty("refreshToken").GetString();

        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/identity/refresh",
            new { refreshToken },
            CancellationToken.None);
        var refreshedTokens = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(refreshedTokens.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(refreshedTokens.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Me_returns_the_authenticated_guid_and_email()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();
        await SeedUserAsync(email, ValidPassword);

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = ValidPassword },
            CancellationToken.None);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var accessToken = tokens.GetProperty("accessToken").GetString()!;

        using var response = await GetMeAsync(client, accessToken);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, account.GetProperty("id").GetGuid());
        Assert.Equal(email, account.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Me_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/api/identity/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Only_login_and_refresh_identity_endpoints_are_marked_anonymous()
    {
        using var client = _application.CreateClient();
        var anonymousIdentityEndpoints = _application.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.DisplayName?.Contains("/api/identity/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(endpoint => endpoint.Metadata.OfType<IAllowAnonymous>().Any())
            .Select(endpoint => endpoint.DisplayName!)
            .ToArray();

        Assert.Equal(2, anonymousIdentityEndpoints.Length);
        Assert.Contains(anonymousIdentityEndpoints, endpoint => endpoint.EndsWith("/api/identity/login", StringComparison.Ordinal));
        Assert.Contains(anonymousIdentityEndpoints, endpoint => endpoint.EndsWith("/api/identity/refresh", StringComparison.Ordinal));
    }

    private const string ValidPassword = "P@ssw0rd!2026";

    private static string UniqueEmail() => $"master.mariner.{Guid.NewGuid():N}@harborops.test";

    private Task SeedUserAsync(string email, string password) =>
        _application.CreateTestUserAsync(email, password);

    private static async Task<HttpResponseMessage> GetMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, CancellationToken.None);
    }
}
