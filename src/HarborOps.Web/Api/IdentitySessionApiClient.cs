using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HarborOps.Web.Api;

internal sealed class IdentitySessionApiClient(HttpClient httpClient)
{
    internal async Task<IdentityAccessTokenResponse?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/identity/login", new { email, password }, cancellationToken);
        return response.StatusCode == HttpStatusCode.Unauthorized
            ? null
            : response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<IdentityAccessTokenResponse>(cancellationToken)
                : throw new HttpRequestException("The identity service could not complete sign-in.");
    }

    internal async Task<IdentityAccessTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/identity/refresh", new { refreshToken }, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IdentityAccessTokenResponse>(cancellationToken)
            : null;
    }

    internal async Task<CurrentAccountResponse?> GetCurrentAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/identity/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CurrentAccountResponse>(cancellationToken)
            : null;
    }

    internal async Task<bool> HasAdministrationPermissionAsync(string accessToken, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.StatusCode == HttpStatusCode.BadRequest;
    }
}
