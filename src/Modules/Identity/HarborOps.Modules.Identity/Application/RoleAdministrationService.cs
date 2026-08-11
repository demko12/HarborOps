using System.Security.Claims;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Identity.Application;

internal sealed class RoleAdministrationService(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<IdentityUser<Guid>> userManager,
    IdentityAdministratorGuard administratorGuard)
{
    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await roleManager.Roles.OrderBy(role => role.Name).ToListAsync(cancellationToken);
        var responses = new List<RoleResponse>(roles.Count);

        foreach (var role in roles)
        {
            var permissions = (await roleManager.GetClaimsAsync(role))
                .Where(claim => claim.Type == Permissions.ClaimType)
                .Select(claim => claim.Value)
                .ToArray();
            responses.Add(new RoleResponse(role.Id, role.Name!, permissions));
        }

        return responses;
    }

    public async Task<RoleResponse> CreateAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AdminValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Name)] = ["Role name is required."],
            });
        }

        if (await roleManager.RoleExistsAsync(request.Name))
        {
            throw new DuplicateRoleNameException(request.Name);
        }

        var role = new IdentityRole<Guid>(request.Name);
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            throw new AdminValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Name)] = result.Errors.Select(error => error.Description).ToArray(),
            });
        }

        return new RoleResponse(role.Id, role.Name!, []);
    }

    public async Task AssignUserRolesAsync(
        Guid userId,
        AssignUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new UserNotFoundException(userId);

        var distinctRoleNames = request.RoleNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var roleName in distinctRoleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                throw new RoleNotFoundException(roleName);
            }
        }

        await administratorGuard.EnsureFinalAdministratorPreservedForUserRoleChangeAsync(
            user, distinctRoleNames, cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles
            .Except(distinctRoleNames, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rolesToAdd = distinctRoleNames
            .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rolesToRemove.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, rolesToRemove);
        }
        if (rolesToAdd.Length > 0)
        {
            await userManager.AddToRolesAsync(user, rolesToAdd);
        }

        // Bounds stale-token exposure: the current access token still carries the
        // old claims until it expires, but the next /refresh will fail security
        // stamp validation and force re-login with the corrected role set.
        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task AssignRolePermissionsAsync(
        Guid roleId,
        AssignRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByIdAsync(roleId.ToString())
            ?? throw new RoleNotFoundException(roleId);

        var distinctPermissions = request.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        var knownPermissionNames = Permissions.Catalog.Select(permission => permission.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = distinctPermissions.Where(name => !knownPermissionNames.Contains(name)).ToArray();
        if (unknown.Length > 0)
        {
            throw new AdminValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Permissions)] = unknown
                    .Select(name => $"Unknown permission '{name}'.")
                    .ToArray(),
            });
        }

        await administratorGuard.EnsureFinalAdministratorPreservedForRolePermissionChangeAsync(
            role, distinctPermissions, cancellationToken);

        var currentClaims = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Permissions.ClaimType)
            .ToArray();

        foreach (var claim in currentClaims)
        {
            await roleManager.RemoveClaimAsync(role, claim);
        }
        foreach (var permission in distinctPermissions)
        {
            await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
        }

        var affectedUsers = await userManager.GetUsersInRoleAsync(role.Name!);
        foreach (var affectedUser in affectedUsers)
        {
            await userManager.UpdateSecurityStampAsync(affectedUser);
        }
    }
}
