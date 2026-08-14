using System.Security.Claims;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HarborOps.Modules.Identity.Presentation;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var identityApiGroup = app.MapGroup("/api/identity").WithTags("Identity");
        identityApiGroup.MapIdentityApi<IdentityUser<Guid>>()
            .AllowAnonymous()
            .Add(DisableSelfRegistration);

        var meGroup = app.MapGroup("/api/identity").WithTags("Identity");
        meGroup.MapGet("/me", async (ClaimsPrincipal principal, UserManager<IdentityUser<Guid>> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new AccountResponse(
                    user.Id,
                    user.Email!,
                    principal.FindAll(Permissions.ClaimType)
                        .Select(claim => claim.Value)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(permission => permission, StringComparer.Ordinal)
                        .ToArray()));
        })
            .RequireAuthorization();

        app.MapIdentityAdminEndpoints();

        return app;
    }


    private static void DisableSelfRegistration(EndpointBuilder endpointBuilder)
    {
        if (endpointBuilder is RouteEndpointBuilder { RoutePattern.RawText: "/register" } routeEndpointBuilder)
        {
            routeEndpointBuilder.RequestDelegate = context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            };
        }
    }
}
