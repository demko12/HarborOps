using System.Security.Claims;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Http;

namespace HarborOps.Web.Tests.Api;

public sealed class CurrentUserAuthorizationStateTests
{
    [Fact]
    public void CanManageAdministration_requires_both_user_and_role_management_permissions()
    {
        var state = CreateState(CurrentUserAuthorizationState.ManageUsersPermission);

        Assert.False(state.CanManageAdministration);
    }

    [Fact]
    public void CanManageAdministration_allows_an_administrator_with_both_management_permissions()
    {
        var state = CreateState(
            CurrentUserAuthorizationState.ManageUsersPermission,
            CurrentUserAuthorizationState.ManageRolesPermission);

        Assert.True(state.CanManageAdministration);
    }

    private static CurrentUserAuthorizationState CreateState(params string[] permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }
}
