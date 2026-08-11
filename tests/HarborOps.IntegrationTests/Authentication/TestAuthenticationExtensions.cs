using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Authentication;

public static class TestAuthenticationExtensions
{
    private const string TestUserPassword = "P@ssw0rd!2026";

    
    public static Task<HttpClient> CreateAuthenticatedClientAsync(this HarborOpsApiFactory application)
    {
        return application.CreateAuthenticatedClientAsync(roleNames: []);
    }

   
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        this HarborOpsApiFactory application,
        params string[] roleNames)
    {
        var email = $"integration-test.{Guid.NewGuid():N}@harborops.test";
        await application.CreateTestUserAsync(email, TestUserPassword, roleNames);

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

  
    public static async Task<HttpClient> CreateAuthenticatedClientWithPermissionsAsync(
        this HarborOpsApiFactory application,
        params string[] permissions)
    {
        var roleName = $"test-role-{Guid.NewGuid():N}";
        await application.CreateTestRoleAsync(roleName, permissions);
        return await application.CreateAuthenticatedClientAsync(roleName);
    }

    public static async Task CreateTestUserAsync(
        this HarborOpsApiFactory application,
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

    public static async Task CreateTestRoleAsync(
        this HarborOpsApiFactory application,
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
