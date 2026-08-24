using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace HarborOps.IntegrationTests.Identity;

public sealed class IdentityAdminEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public IdentityAdminEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_users_creates_account_with_temporary_password()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);
        var email = $"new.hire.{Guid.NewGuid():N}@harborops.test";

        using var response = await client.PostAsJsonAsync(
            "/api/identity/admin/users",
            new CreateUserRequest(email, "Temp0rary!Passw0rd"),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CreateUserResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(email, result.Email);
        Assert.Equal($"/api/identity/admin/users/{result.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Post_users_returns_409_for_duplicate_email()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);
        var email = $"dup.{Guid.NewGuid():N}@harborops.test";
        var request = new CreateUserRequest(email, "Temp0rary!Passw0rd");

        using var firstResponse = await client.PostAsJsonAsync("/api/identity/admin/users", request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/identity/admin/users", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Post_users_returns_400_for_missing_fields()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);

        using var response = await client.PostAsJsonAsync(
            "/api/identity/admin/users",
            new CreateUserRequest(string.Empty, string.Empty),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_users_supports_search_and_paging()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);
        var marker = Guid.NewGuid().ToString("N")[..8];

        for (var i = 0; i < 3; i++)
        {
            await _application.CreateTestUserAsync($"{marker}.user{i}@harborops.test", "P@ssw0rd!2026");
        }

        using var response = await client.GetAsync(
            $"/api/identity/admin/users?search={marker}&pageNumber=1&pageSize=2",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchUsersResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task Post_roles_creates_role()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityRoles.Manage);
        var roleName = $"role-{Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync(
            "/api/identity/admin/roles",
            new CreateRoleRequest(roleName),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RoleResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(roleName, result.Name);
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public async Task Post_roles_returns_409_for_duplicate_name()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityRoles.Manage);
        var roleName = $"dup-role-{Guid.NewGuid():N}";
        var request = new CreateRoleRequest(roleName);

        using var firstResponse = await client.PostAsJsonAsync("/api/identity/admin/roles", request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/identity/admin/roles", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Put_role_permissions_replaces_the_complete_set()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityRoles.Manage);
        var roleName = $"role-{Guid.NewGuid():N}";
        await _application.CreateTestRoleAsync(roleName, Permissions.Fleet.Read);
        var roleId = await GetRoleIdAsync(client, roleName);

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/roles/{roleId}/permissions",
            new AssignRolePermissionsRequest([Permissions.Crew.Read, Permissions.Crew.Manage]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var roles = await GetRolesAsync(client);
        var updated = roles.Single(role => role.Id == roleId);
        Assert.Equal(2, updated.Permissions.Count);
        Assert.Contains(Permissions.Crew.Read, updated.Permissions);
        Assert.Contains(Permissions.Crew.Manage, updated.Permissions);
        Assert.DoesNotContain(Permissions.Fleet.Read, updated.Permissions);
    }

    [Fact]
    public async Task Put_role_permissions_returns_400_for_unknown_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityRoles.Manage);
        var roleName = $"role-{Guid.NewGuid():N}";
        await _application.CreateTestRoleAsync(roleName);
        var roleId = await GetRoleIdAsync(client, roleName);

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/roles/{roleId}/permissions",
            new AssignRolePermissionsRequest(["not.a.real.permission"]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_role_permissions_returns_404_for_unknown_role()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityRoles.Manage);

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/roles/{Guid.NewGuid()}/permissions",
            new AssignRolePermissionsRequest([Permissions.Crew.Read]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_user_roles_returns_404_for_unknown_user()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/users/{Guid.NewGuid()}/roles",
            new AssignUserRolesRequest([]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_user_roles_returns_404_for_unknown_role_name()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);
        var email = $"target.{Guid.NewGuid():N}@harborops.test";
        await _application.CreateTestUserAsync(email, "P@ssw0rd!2026");
        var userId = await GetUserIdAsync(client, email);

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/users/{userId}/roles",
            new AssignUserRolesRequest(["role-that-does-not-exist"]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Account_lifecycle_actions_require_manage_permission()
    {
        using var readOnlyClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);

        using var response = await readOnlyClient.PostAsync(
            $"/api/identity/admin/users/{Guid.NewGuid()}/disable",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Disable_and_reactivate_update_account_state_and_invalidate_existing_access()
    {
        using var administratorClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage,
            Permissions.IdentityRoles.Manage);
        var email = $"lifecycle.{Guid.NewGuid():N}@harborops.test";
        const string password = "P@ssw0rd!2026";
        await _application.CreateTestUserAsync(email, password);
        var userId = await GetUserIdAsync(administratorClient, email);

        using var userClient = _application.CreateClient();
        using var loginResponse = await userClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password },
            CancellationToken.None);
        var accessToken = (await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken.None))
            .GetProperty("accessToken")
            .GetString();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var disableResponse = await administratorClient.PostAsync(
            $"/api/identity/admin/users/{userId}/disable",
            content: null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        using var disabledAccessResponse = await userClient.GetAsync("/api/identity/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, disabledAccessResponse.StatusCode);

        using var reactivateResponse = await administratorClient.PostAsync(
            $"/api/identity/admin/users/{userId}/reactivate",
            content: null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, reactivateResponse.StatusCode);

        using var reloginResponse = await userClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    [Fact]
    public async Task Temporary_password_replaces_the_old_password()
    {
        using var administratorClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);
        var email = $"password-reset.{Guid.NewGuid():N}@harborops.test";
        const string oldPassword = "P@ssw0rd!2026";
        const string newPassword = "NewTemp0rary!Passw0rd";
        await _application.CreateTestUserAsync(email, oldPassword);
        var userId = await GetUserIdAsync(administratorClient, email);

        using var resetResponse = await administratorClient.PostAsJsonAsync(
            $"/api/identity/admin/users/{userId}/temporary-password",
            new SetTemporaryPasswordRequest(newPassword),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        using var signInClient = _application.CreateClient();
        using var oldPasswordResponse = await signInClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = oldPassword },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);

        using var newPasswordResponse = await signInClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password = newPassword },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, newPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task Revoke_sessions_invalidates_existing_access_without_disabling_the_account()
    {
        using var administratorClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);
        var email = $"revoke.{Guid.NewGuid():N}@harborops.test";
        const string password = "P@ssw0rd!2026";
        await _application.CreateTestUserAsync(email, password);
        var userId = await GetUserIdAsync(administratorClient, email);

        using var userClient = _application.CreateClient();
        using var loginResponse = await userClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password },
            CancellationToken.None);
        var accessToken = (await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken.None))
            .GetProperty("accessToken")
            .GetString();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var revokeResponse = await administratorClient.PostAsync(
            $"/api/identity/admin/users/{userId}/revoke-sessions",
            content: null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var revokedAccessResponse = await userClient.GetAsync("/api/identity/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccessResponse.StatusCode);

        using var reloginResponse = await userClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    [Fact]
    public async Task Disable_rejects_an_administrators_own_account()
    {
        var roleName = $"self-lifecycle-{Guid.NewGuid():N}";
        var email = $"self.lifecycle.{Guid.NewGuid():N}@harborops.test";
        const string password = "P@ssw0rd!2026";
        await _application.CreateTestRoleAsync(roleName, Permissions.IdentityUsers.Manage);
        await _application.CreateTestUserAsync(email, password, roleName);

        using var client = _application.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login",
            new { email, password },
            CancellationToken.None);
        var accessToken = (await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken.None))
            .GetProperty("accessToken")
            .GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var userId = await GetUserIdAsync(client, email);

        using var response = await client.PostAsync(
            $"/api/identity/admin/users/{userId}/disable",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Disable_preserves_the_last_effective_identity_administrator()
    {
        var operatorRoleName = $"lifecycle-operator-{Guid.NewGuid():N}";
        var administratorRoleName = $"lifecycle-last-admin-{Guid.NewGuid():N}";
        var operatorEmail = $"lifecycle.operator.{Guid.NewGuid():N}@harborops.test";
        var administratorEmail = $"lifecycle.admin.{Guid.NewGuid():N}@harborops.test";
        const string password = "P@ssw0rd!2026";
        await _application.CreateTestRoleAsync(operatorRoleName, Permissions.IdentityUsers.Manage);
        await _application.CreateTestRoleAsync(
            administratorRoleName,
            Permissions.IdentityUsers.Manage,
            Permissions.IdentityRoles.Manage);
        await _application.CreateTestUserAsync(operatorEmail, password, operatorRoleName);
        await _application.CreateTestUserAsync(administratorEmail, password, administratorRoleName);

        using var operatorClient = _application.CreateClient();
        using var loginResponse = await operatorClient.PostAsJsonAsync(
            "/api/identity/login",
            new { email = operatorEmail, password },
            CancellationToken.None);
        var accessToken = (await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken.None))
            .GetProperty("accessToken")
            .GetString();
        operatorClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var administratorId = await GetUserIdAsync(operatorClient, administratorEmail);

        using var response = await operatorClient.PostAsync(
            $"/api/identity/admin/users/{administratorId}/disable",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }


    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string roleName)
    {
        var roles = await GetRolesAsync(client);
        return roles.Single(role => role.Name == roleName).Id;
    }

    private static async Task<List<RoleResponse>> GetRolesAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/admin/roles", CancellationToken.None);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleResponse>>(CancellationToken.None);
        return roles ?? [];
    }

    private static async Task<Guid> GetUserIdAsync(HttpClient client, string email)
    {
        using var response = await client.GetAsync(
            $"/api/identity/admin/users?search={email}&pageNumber=1&pageSize=10",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchUsersResponse>(CancellationToken.None);
        return result!.Items.Single(user => user.Email == email).Id;
    }
}
