using System.Security.Claims;
using HarborOps.Modules.Identity.Application;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace HarborOps.Modules.Identity.Presentation;

public static class IdentityEndpoints
{
    public const string UnavailableEndpointPolicy = "identity.unavailable-endpoint";

    public static IEndpointRouteBuilder MapIdentityModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var identityApiGroup = app.MapGroup("/api/identity").WithTags("Identity");
        identityApiGroup.MapIdentityApi<IdentityUser<Guid>>()
            .Add(ConfigureGeneratedIdentityEndpoint);

        var meGroup = app.MapGroup("/api/identity").WithTags("Identity");
        meGroup.MapGet("/me", async (ClaimsPrincipal principal, UserManager<IdentityUser<Guid>> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal);
            if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow)
            {
                return Results.Unauthorized();
            }

            var tokenSecurityStamp = principal.FindFirstValue(userManager.Options.ClaimsIdentity.SecurityStampClaimType);
            return !string.IsNullOrWhiteSpace(tokenSecurityStamp)
                   && !string.Equals(tokenSecurityStamp, user.SecurityStamp, StringComparison.Ordinal)
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

        var accountGroup = app.MapGroup("/api/identity/account").WithTags("Identity Account");
        accountGroup.MapPost("/change-password", async (
                ChangePasswordRequest request,
                ClaimsPrincipal principal,
                AccountSecurityService service) =>
            {
                try
                {
                    await service.ChangePasswordAsync(principal, request);
                    return Results.NoContent();
                }
                catch (AccountSecurityValidationException exception)
                {
                    return Results.ValidationProblem(exception.Errors, statusCode: StatusCodes.Status400BadRequest);
                }
                catch (AccountSecurityUnauthorizedException)
                {
                    return Results.Unauthorized();
                }
            })
            .RequireAuthorization();

        accountGroup.MapPost("/revoke-sessions", async (
                ClaimsPrincipal principal,
                AccountSecurityService service) =>
            {
                try
                {
                    await service.RevokeSessionsAsync(principal);
                    return Results.NoContent();
                }
                catch (AccountSecurityUnauthorizedException)
                {
                    return Results.Unauthorized();
                }
            })
            .RequireAuthorization();

        app.MapIdentityAdminEndpoints();

        return app;
    }


    private static void ConfigureGeneratedIdentityEndpoint(EndpointBuilder endpointBuilder)
    {
        for (var index = endpointBuilder.Metadata.Count - 1; index >= 0; index--)
        {
            if (endpointBuilder.Metadata[index] is IAllowAnonymous)
            {
                endpointBuilder.Metadata.RemoveAt(index);
            }
        }

        if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder)
        {
            return;
        }

        var routeName = routeEndpointBuilder.RoutePattern.PathSegments
            .SelectMany(segment => segment.Parts)
            .OfType<RoutePatternLiteralPart>()
            .Select(part => part.Content)
            .LastOrDefault();
        var httpMethods = endpointBuilder.Metadata
            .OfType<IHttpMethodMetadata>()
            .SelectMany(metadata => metadata.HttpMethods)
            .ToArray();

        if (IsAnonymousEndpoint(routeName, httpMethods))
        {
            endpointBuilder.Metadata.Add(new AllowAnonymousAttribute());
            return;
        }

        if (IsUnavailablePublicEndpoint(routeName))
        {
            endpointBuilder.Metadata.Add(new AuthorizeAttribute(UnavailableEndpointPolicy));
            routeEndpointBuilder.RequestDelegate = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            };
        }
    }

    // HarborOps supports anonymous token issuance only; all other generated Identity routes
    // are protected by the fallback policy or explicitly hidden below.
    private static bool IsAnonymousEndpoint(string? routeName, IReadOnlyList<string> httpMethods) =>
        httpMethods.Contains(HttpMethods.Post, StringComparer.OrdinalIgnoreCase) &&
        routeName is "login" or "refresh";

    private static bool IsUnavailablePublicEndpoint(string? routeName) =>
        routeName is
            "register" or
            "confirmEmail" or
            "resendConfirmationEmail" or
            "forgotPassword" or
            "resetPassword";
}
