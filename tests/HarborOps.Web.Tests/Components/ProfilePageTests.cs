using System.Reflection;
using System.Security.Claims;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class ProfilePageTests : BunitContext
{
    [Fact]
    public void Profile_renders_the_current_account_and_grouped_permissions()
    {
        var page = RenderProfile(
            "operator@harborops.test",
            "50000000-0000-0000-0000-000000000001",
            "fleet.read",
            "fleet.manage",
            "crew.read");

        Assert.Contains("operator@harborops.test", page.Markup, StringComparison.Ordinal);
        Assert.Contains("50000000-0000-0000-0000-000000000001", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Fleet", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Read", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Manage", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Crew", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_shows_a_clear_empty_state_when_the_account_has_no_permissions()
    {
        var page = RenderProfile("viewer@harborops.test", "50000000-0000-0000-0000-000000000002");

        Assert.Contains("No permissions assigned", page.Markup, StringComparison.Ordinal);
        Assert.Contains("no operational permissions", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profile_route_requires_an_authenticated_user()
    {
        var authorization = typeof(Profile).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.True(string.IsNullOrEmpty(authorization.Policy));
    }

    private IRenderedComponent<Profile> RenderProfile(string email, string userId, params string[] permissions)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));

        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        Services.AddScoped<CurrentUserAuthorizationState>();

        return Render<Profile>();
    }
}
