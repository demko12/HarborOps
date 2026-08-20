using System.Security.Claims;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HarborOps.Modules.Identity.Application;


public sealed class IdentityBootstrapSeeder(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<IdentityBootstrapSeeder> logger) : IHostedService
{
    public const string BootstrapAdministratorRoleName = "IdentityAdministrator";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["Identity:BootstrapAdmin:Email"];
        var password = configuration["Identity:BootstrapAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Identity bootstrap admin configuration not supplied; skipping bootstrap seed.");
            return;
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var role = await roleManager.FindByNameAsync(BootstrapAdministratorRoleName);
        if (role is null)
        {
            role = new IdentityRole<Guid>(BootstrapAdministratorRoleName);
            await roleManager.CreateAsync(role);
            logger.LogInformation("Created bootstrap administrator role.");
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        foreach (var permission in new[]
        {
            Permissions.IdentityUsers.Manage,
            Permissions.IdentityRoles.Manage,
            Permissions.Voyages.Read,
            Permissions.Voyages.Manage,
            Permissions.Fleet.Read,
            Permissions.Maintenance.Read,
            Permissions.Maintenance.Manage,
        })
        {
            var alreadyGranted = existingClaims.Any(claim =>
                claim.Type == Permissions.ClaimType && claim.Value == permission);
            if (!alreadyGranted)
            {
                await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
            }
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser<Guid> { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                // Never log error.Description here — Identity's password validators
                // echo the offending value back in some messages. Codes only.
                logger.LogError(
                    "Failed to seed bootstrap administrator account. Error codes: {ErrorCodes}",
                    string.Join(", ", createResult.Errors.Select(error => error.Code)));
                return;
            }

            logger.LogInformation("Seeded bootstrap administrator account.");
        }

        if (!await userManager.IsInRoleAsync(user, BootstrapAdministratorRoleName))
        {
            await userManager.AddToRoleAsync(user, BootstrapAdministratorRoleName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
