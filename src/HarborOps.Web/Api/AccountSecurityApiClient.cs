using System.Net;
using System.Net.Http.Json;

namespace HarborOps.Web.Api;

internal sealed class AccountSecurityApiClient(HttpClient httpClient)
{
    internal async Task<AccountSecurityApiResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/identity/account/change-password", new { currentPassword, newPassword }, cancellationToken);
        return new AccountSecurityApiResult(response.StatusCode);
    }

    internal async Task<AccountSecurityApiResult> RevokeSessionsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/identity/account/revoke-sessions", new { }, cancellationToken);
        return new AccountSecurityApiResult(response.StatusCode);
    }
}

internal sealed record AccountSecurityApiResult(HttpStatusCode StatusCode)
{
    internal bool Succeeded => StatusCode == HttpStatusCode.NoContent;
    internal bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
}
