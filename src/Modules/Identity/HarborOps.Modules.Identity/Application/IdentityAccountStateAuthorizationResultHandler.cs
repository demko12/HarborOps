using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace HarborOps.Modules.Identity.Application;

/// <summary>
/// Turns lifecycle-invalidated bearer sessions into an authentication challenge so
/// HarborOps.Web can clear its server-side ticket after refresh is rejected.
/// </summary>
internal sealed class IdentityAccountStateAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && context.User.HasClaim(AccountLifecycleClaims.InvalidatedClaimType, "true"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
