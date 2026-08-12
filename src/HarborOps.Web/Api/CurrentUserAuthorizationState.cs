using System.Security.Claims;

namespace HarborOps.Web.Api;

/// <summary>Web-owned display state only; API policies remain authoritative.</summary>
internal sealed class CurrentUserAuthorizationState(IHttpContextAccessor httpContextAccessor)
{
    internal const string PermissionClaimType = "harborops:permission";
    internal const string ManageUsersPermission = "identity.users.manage";
    internal const string ReadUsersPermission = "identity.users.read";
    internal const string ManageRolesPermission = "identity.roles.manage";
    internal const string ReadRolesPermission = "identity.roles.read";

    internal event Action? Changed;

    internal Guid? UserId => Guid.TryParse(
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : null;

    internal bool CanAccessAdministration => HasPermission(ReadUsersPermission) || HasPermission(ManageUsersPermission)
        || HasPermission(ReadRolesPermission) || HasPermission(ManageRolesPermission);

    internal bool CanManageAdministration => HasPermission(ManageUsersPermission)
        && HasPermission(ManageRolesPermission);

    internal bool HasPermission(string permission) => httpContextAccessor.HttpContext?.User.HasClaim(PermissionClaimType, permission) == true;

    internal void RefreshVisibleAuthorization() => Changed?.Invoke();
}
