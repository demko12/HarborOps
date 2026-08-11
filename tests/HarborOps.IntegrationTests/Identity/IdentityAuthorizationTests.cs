using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;

namespace HarborOps.IntegrationTests.Identity;

public sealed class IdentityAuthorizationTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public IdentityAuthorizationTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Admin_endpoint_returns_401_when_anonymous()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/api/identity/admin/users", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_endpoint_returns_403_when_authenticated_without_permission()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/identity/admin/users", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_endpoint_permits_access_with_exact_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);

        using var response = await client.GetAsync("/api/identity/admin/users", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Manage_permission_implies_read_on_admin_endpoint()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);

        using var response = await client.GetAsync("/api/identity/admin/users", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Read_permission_does_not_imply_manage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);

        using var response = await client.PostAsJsonAsync(
            "/api/identity/admin/users",
            new CreateUserRequest($"blocked.{Guid.NewGuid():N}@harborops.test", "Temp0rary!Passw0rd"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_remove_final_administrators_role_assignment()
    {
        var (client, roleName, userId) = await CreateSoleIdentityAdministratorAsync();

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/users/{userId}/roles",
            new AssignUserRolesRequest([]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Cannot_strip_manage_permissions_from_the_final_administrators_role()
    {
        var (client, roleName, _) = await CreateSoleIdentityAdministratorAsync();
        var roleId = (await GetRolesAsync(client)).Single(role => role.Name == roleName).Id;

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/roles/{roleId}/permissions",
            new AssignRolePermissionsRequest([Permissions.IdentityUsers.Manage]), // drops roles.manage
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Can_reassign_administrator_role_when_a_second_administrator_still_qualifies()
    {
        var soleAdminRoleName = $"admin-role-{Guid.NewGuid():N}";
        await _application.CreateTestRoleAsync(
            soleAdminRoleName, Permissions.IdentityUsers.Manage, Permissions.IdentityRoles.Manage);

        using var client = await _application.CreateAuthenticatedClientAsync(soleAdminRoleName);
        var firstAdminId = (await GetUsersAsync(client)).Items.First(u => u.Roles.Contains(soleAdminRoleName)).Id;

        // Second administrator exists — removing the first's admin role should now be allowed.
        await _application.CreateTestUserAsync(
            $"second.admin.{Guid.NewGuid():N}@harborops.test", "P@ssw0rd!2026", soleAdminRoleName);

        using var response = await client.PutAsJsonAsync(
            $"/api/identity/admin/users/{firstAdminId}/roles",
            new AssignUserRolesRequest([]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Response_never_includes_password_or_token_fields()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Manage);
        var email = $"secure.{Guid.NewGuid():N}@harborops.test";

        using var response = await client.PostAsJsonAsync(
            "/api/identity/admin/users",
            new CreateUserRequest(email, "Temp0rary!Passw0rd"),
            CancellationToken.None);
        var raw = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.DoesNotContain("Temp0rary!Passw0rd", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(HttpClient Client, string RoleName, Guid UserId)> CreateSoleIdentityAdministratorAsync()
    {
        var roleName = $"admin-role-{Guid.NewGuid():N}";
        await _application.CreateTestRoleAsync(
            roleName, Permissions.IdentityUsers.Manage, Permissions.IdentityRoles.Manage);

        var client = await _application.CreateAuthenticatedClientAsync(roleName);
        var users = await GetUsersAsync(client);
        var userId = users.Items.First(user => user.Roles.Contains(roleName)).Id;

        return (client, roleName, userId);
    }

    private static async Task<SearchUsersResponse> GetUsersAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/admin/users?pageSize=200", CancellationToken.None);
        return (await response.Content.ReadFromJsonAsync<SearchUsersResponse>(CancellationToken.None))!;
    }

    private static async Task<List<RoleResponse>> GetRolesAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/admin/roles", CancellationToken.None);
        return (await response.Content.ReadFromJsonAsync<List<RoleResponse>>(CancellationToken.None))!;
    }
}
