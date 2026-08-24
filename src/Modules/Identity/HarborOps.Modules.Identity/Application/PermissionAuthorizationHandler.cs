using System.Security.Claims;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace HarborOps.Modules.Identity.Application;


public sealed class PermissionAuthorizationHandler(
    UserManager<IdentityUser<Guid>> userManager) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (await IsAccountInvalidatedAsync(context.User))
        {
            MarkAccountInvalidated(context.User);
            context.Fail();
            return;
        }

        var required = requirement.Permission;
        var lastDot = required.LastIndexOf('.');
        var capability = required[..lastDot];
        var action = required[(lastDot + 1)..];

        var grantedPermissions = context.User
            .FindAll(Permissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        var manageEquivalent = $"{capability}.manage";

        if (grantedPermissions.Contains(required) ||
            (action == "read" && grantedPermissions.Contains(manageEquivalent)))
        {
            context.Succeed(requirement);
        }

    }

    private async Task<bool> IsAccountInvalidatedAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return true;
        }

        var tokenSecurityStamp = principal.FindFirstValue(userManager.Options.ClaimsIdentity.SecurityStampClaimType);
        return !string.IsNullOrWhiteSpace(tokenSecurityStamp)
            && !string.Equals(tokenSecurityStamp, user.SecurityStamp, StringComparison.Ordinal);
    }

    private static void MarkAccountInvalidated(ClaimsPrincipal principal)
    {
        if (!principal.HasClaim(AccountLifecycleClaims.InvalidatedClaimType, "true"))
        {
            principal.Identities.FirstOrDefault()?.AddClaim(new Claim(AccountLifecycleClaims.InvalidatedClaimType, "true"));
        }
    }
}
