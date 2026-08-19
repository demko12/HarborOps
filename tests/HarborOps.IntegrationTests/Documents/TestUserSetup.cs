using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

/// <summary>
/// Mirrors <see cref="HarborOps.IntegrationTests.Authentication.TestAuthenticationExtensions"/>
/// but as plain static methods over <see cref="WebApplicationFactory{Program}"/> instead of
/// extension methods on the sealed <see cref="HarborOpsApiFactory"/>, since
/// <see cref="DocumentFileStorageApiFactory"/> is a different factory type and extension
/// methods with the same name in this namespace could shadow the existing ones for other
/// Documents test files that still use <see cref="HarborOpsApiFactory"/>.
/// </summary>
internal static class TestUserSetup
{
    private const string TestUserPassword = "P@ssw0rd!2026";

    internal static Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<Program> application)
    {
        return CreateAuthenticatedClientAsync(application, roleNames: []);
    }

    internal static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> application,
        params string[] roleNames)
    {
        var email = $"integration-test.{Guid.NewGuid():N}@harborops.test";
        await CreateTestUserAsync(application, email, TestUserPassword, roleNames);

        var client = application.CreateClient();

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

    internal static async Task<HttpClient> CreateAuthenticatedClientWithPermissionsAsync(
        WebApplicationFactory<Program> application,
        params string[] permissions)
    {
        var roleName = $"test-role-{Guid.NewGuid():N}";
        await CreateTestRoleAsync(application, roleName, permissions);
        return await CreateAuthenticatedClientAsync(application, roleName);
    }

    /// <summary>
    /// Same as <see cref="CreateAuthenticatedClientWithPermissionsAsync"/> but also resolves
    /// the created test user's Guid id via <c>/api/identity/me</c>, for tests asserting an
    /// "uploaded by this authenticated user" identity.
    /// </summary>
    internal static async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientWithPermissionsAndIdAsync(
        WebApplicationFactory<Program> application,
        params string[] permissions)
    {
        var client = await CreateAuthenticatedClientWithPermissionsAsync(application, permissions);

        using var meResponse = await client.GetAsync("/api/identity/me", CancellationToken.None);
        meResponse.EnsureSuccessStatusCode();
        var account = await meResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var userId = account.GetProperty("id").GetGuid();

        return (client, userId);
    }

    private static async Task CreateTestUserAsync(
        WebApplicationFactory<Program> application,
        string email,
        string password,
        params string[] roleNames)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var user = new IdentityUser<Guid> { UserName = email, Email = email, EmailConfirmed = true };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        foreach (var roleName in roleNames)
        {
            if (await roleManager.FindByNameAsync(roleName) is null)
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        if (roleNames.Length > 0)
        {
            await userManager.AddToRolesAsync(user, roleNames);
        }
    }

    private static async Task CreateTestRoleAsync(
        WebApplicationFactory<Program> application,
        string roleName,
        params string[] permissions)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new IdentityRole<Guid>(roleName);
            await roleManager.CreateAsync(role);
        }

        foreach (var permission in permissions)
        {
            await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
        }
    }
}
