using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

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
    public async Task Register_with_valid_data_succeeds_and_persists_the_account()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();

        using var response = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password = ValidPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Confirm the account can actually authenticate afterwards, proving persistence
        // rather than just a 200 response.
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = ValidPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Register_with_duplicate_email_fails_safely()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password = ValidPassword },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password = ValidPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);

        var problem = await secondResponse.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Register_with_invalid_password_fails_safely()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email = UniqueEmail(), password = "weak" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_usable_access_and_refresh_tokens()
    {
        using var client = _application.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email, ValidPassword);

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
        await RegisterAsync(client, registeredEmail, ValidPassword);

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
        await RegisterAsync(client, email, ValidPassword);

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
        await RegisterAsync(client, email, ValidPassword);

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

    private const string ValidPassword = "P@ssw0rd!2026";

    private static string UniqueEmail() => $"master.mariner.{Guid.NewGuid():N}@harborops.test";

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, CancellationToken.None);
    }
}
