using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.IntegrationTests.Authentication;

public static class TestAuthenticationExtensions
{
    private const string TestUserPassword = "P@ssw0rd!2026";

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(this HarborOpsApiFactory application)
    {
        var client = application.CreateClient();
        var email = $"integration-test.{Guid.NewGuid():N}@harborops.test";

        using var registerResponse = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { email, password = TestUserPassword },
            CancellationToken.None);
        registerResponse.EnsureSuccessStatusCode();

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = TestUserPassword },
            CancellationToken.None);
        loginResponse.EnsureSuccessStatusCode();

        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var accessToken = tokens.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
