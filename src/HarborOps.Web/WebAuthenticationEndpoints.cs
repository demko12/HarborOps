using System.Security.Claims;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HarborOps.Web;

internal static class WebAuthenticationEndpoints
{
    internal static IEndpointRouteBuilder MapWebAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/session/login", (Func<HttpContext, IdentitySessionApiClient, IAntiforgery, CancellationToken, Task<IResult>>)LoginAsync);
        app.MapPost("/session/logout", (Func<HttpContext, IAntiforgery, Task<IResult>>)LogoutAsync);
        return app;
    }

    private static async Task<IResult> LoginAsync(HttpContext context, IdentitySessionApiClient identityApi, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var email = form["email"].ToString();
        var password = form["password"].ToString();
        var returnUrl = SafeReturnUrl(form["returnUrl"].ToString());
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return Results.Redirect($"/login?error=required&returnUrl={Uri.EscapeDataString(returnUrl)}");
        try
        {
            var tokens = await identityApi.LoginAsync(email, password, cancellationToken);
            if (tokens is null) return Results.Redirect($"/login?error=invalid&returnUrl={Uri.EscapeDataString(returnUrl)}");
            var account = await identityApi.GetCurrentAccountAsync(tokens.AccessToken, cancellationToken);
            if (account is null) return Results.Redirect("/login?error=unavailable");
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.Name, account.Email));
            identity.AddClaim(new Claim(ClaimTypes.Email, account.Email));
            foreach (var permission in (account.Permissions ?? []).Distinct(StringComparer.Ordinal))
            {
                identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
            }
            var properties = new AuthenticationProperties { IsPersistent = true };
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = tokens.AccessToken }, new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken }]);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
            return Results.Redirect($"/login?success=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
        catch (HttpRequestException) { return Results.Redirect("/login?error=unavailable"); }
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, IAntiforgery antiforgery) { await antiforgery.ValidateRequestAsync(context); await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.Redirect("/login"); }

    internal static string SafeReturnUrl(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith("/", StringComparison.Ordinal) && !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : "/";
}
