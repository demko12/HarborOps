using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace HarborOps.Modules.Identity.Application;


public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
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

        return Task.CompletedTask;
    }
}
