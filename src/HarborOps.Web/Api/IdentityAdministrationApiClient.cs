using System.Net;
using System.Net.Http.Json;

namespace HarborOps.Web.Api;

internal sealed class IdentityAdministrationApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/identity/admin";

    internal Task<AdminApiResult<AdminUserPage>> SearchUsersAsync(
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken) =>
        SendAsync<AdminUserPage>(HttpMethod.Get,
            $"{BasePath}/users?search={Uri.EscapeDataString(search ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}",
            null,
            cancellationToken);

    internal Task<AdminApiResult<AdminUserSummary>> CreateUserAsync(
        CreateAdminUserRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AdminUserSummary>(HttpMethod.Post, $"{BasePath}/users", request, cancellationToken);

    internal Task<AdminApiResult<IReadOnlyList<AdminRole>>> ListRolesAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<AdminRole>>(HttpMethod.Get, $"{BasePath}/roles", null, cancellationToken);

    internal Task<AdminApiResult<AdminRole>> CreateRoleAsync(
        CreateAdminRoleRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AdminRole>(HttpMethod.Post, $"{BasePath}/roles", request, cancellationToken);

    internal Task<AdminApiResult<object>> ReplaceUserRolesAsync(
        Guid userId,
        ReplaceUserRolesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Put, $"{BasePath}/users/{userId}/roles", request, cancellationToken);

    internal Task<AdminApiResult<object>> DisableUserAsync(Guid userId, CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Post, $"{BasePath}/users/{userId}/disable", null, cancellationToken);

    internal Task<AdminApiResult<object>> ReactivateUserAsync(Guid userId, CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Post, $"{BasePath}/users/{userId}/reactivate", null, cancellationToken);

    internal Task<AdminApiResult<object>> SetTemporaryPasswordAsync(
        Guid userId,
        SetAdminTemporaryPasswordRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Post, $"{BasePath}/users/{userId}/temporary-password", request, cancellationToken);

    internal Task<AdminApiResult<object>> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Post, $"{BasePath}/users/{userId}/revoke-sessions", null, cancellationToken);

    internal Task<AdminApiResult<object>> ReplaceRolePermissionsAsync(
        Guid roleId,
        ReplaceRolePermissionsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Put, $"{BasePath}/roles/{roleId}/permissions", request, cancellationToken);

    private async Task<AdminApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return new AdminApiResult<T>(default, null);
                }

                return new AdminApiResult<T>(await response.Content.ReadFromJsonAsync<T>(cancellationToken), null);
            }

            ApiProblemDetails? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                // Some reverse proxies return an empty or non-problem response. Keep the UI retryable.
            }
            return new AdminApiResult<T>(default, new AdminApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? "Administration request failed",
                problem?.Detail ?? "Please review your access and try again."));
        }
        catch (HttpRequestException)
        {
            return new AdminApiResult<T>(default, new AdminApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }
}
