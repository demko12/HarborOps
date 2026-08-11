using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Identity.Application;


internal sealed class IdentityAdministratorGuard(
    UserManager<IdentityUser<Guid>> userManager,
    RoleManager<IdentityRole<Guid>> roleManager)
{
    public async Task EnsureFinalAdministratorPreservedForUserRoleChangeAsync(
         IdentityUser<Guid> user,
         IReadOnlyCollection<string> newRoleNames,
         CancellationToken cancellationToken)
    {
        var roleGrants = await BuildRolePermissionMapAsync(cancellationToken);

        var countBefore = await CountQualifyingAdministratorsAsync(
            roleGrants, overrideUserId: null, overrideRoleNames: null, cancellationToken);
        var countAfter = await CountQualifyingAdministratorsAsync(
            roleGrants, overrideUserId: user.Id, overrideRoleNames: newRoleNames, cancellationToken);

        if (countBefore > 0 && countAfter == 0)
        {
            throw new FinalIdentityAdministratorException();
        }
    }

    public async Task EnsureFinalAdministratorPreservedForRolePermissionChangeAsync(
        IdentityRole<Guid> role,
        IReadOnlyCollection<string> newPermissions,
        CancellationToken cancellationToken)
    {
        var roleGrants = await BuildRolePermissionMapAsync(cancellationToken);

        var countBefore = await CountQualifyingAdministratorsAsync(
            roleGrants, overrideUserId: null, overrideRoleNames: null, cancellationToken);

        var modifiedGrants = new Dictionary<string, HashSet<string>>(roleGrants, StringComparer.OrdinalIgnoreCase)
        {
            [role.Name!] = newPermissions.ToHashSet(StringComparer.Ordinal),
        };
        var countAfter = await CountQualifyingAdministratorsAsync(
            modifiedGrants, overrideUserId: null, overrideRoleNames: null, cancellationToken);

        if (countBefore > 0 && countAfter == 0)
        {
            throw new FinalIdentityAdministratorException();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> BuildRolePermissionMapAsync(
        CancellationToken cancellationToken)
    {
        var roles = await roleManager.Roles.ToListAsync(cancellationToken);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            map[role.Name!] = claims
                .Where(claim => claim.Type == Permissions.ClaimType)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        return map;
    }

    private async Task<int> CountQualifyingAdministratorsAsync(
         Dictionary<string, HashSet<string>> roleGrants,
         Guid? overrideUserId,
         IReadOnlyCollection<string>? overrideRoleNames,
         CancellationToken cancellationToken)
    {
        var users = await userManager.Users.ToListAsync(cancellationToken);
        var qualifying = 0;

        foreach (var candidate in users)
        {
            string[] roleNames;
            if (candidate.Id == overrideUserId && overrideRoleNames is not null)
            {
                roleNames = overrideRoleNames.ToArray();
            }
            else
            {
                roleNames = (await userManager.GetRolesAsync(candidate)).ToArray();
            }

            var effectivePermissions = roleNames
                .SelectMany(roleName => roleGrants.TryGetValue(roleName, out var grants)
                    ? grants
                    : [])
                .ToHashSet(StringComparer.Ordinal);

            if (effectivePermissions.Contains(Permissions.IdentityUsers.Manage) &&
                effectivePermissions.Contains(Permissions.IdentityRoles.Manage))
            {
                qualifying++;
            }
        }

        return qualifying;
    }
}
