using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace HarborOps.Modules.Identity.Application;

internal static class IdentityAuthorizationPolicies
{
    public static void Configure(AuthorizationOptions options)
    {
        foreach (var permission in Permissions.Catalog)
        {
            options.AddPolicy(permission.Name, policy =>
                policy.Requirements.Add(new PermissionRequirement(permission.Name)));
        }
    }
}
