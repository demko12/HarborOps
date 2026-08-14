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
    internal const string FleetReadPermission = "fleet.read";
    internal const string FleetManagePermission = "fleet.manage";
    internal const string CrewReadPermission = "crew.read";
    internal const string CrewManagePermission = "crew.manage";
    internal const string VoyagesReadPermission = "voyages.read";
    internal const string VoyagesManagePermission = "voyages.manage";
    internal const string CargoReadPermission = "cargo.read";
    internal const string CargoManagePermission = "cargo.manage";
    internal const string MaintenanceReadPermission = "maintenance.read";
    internal const string MaintenanceManagePermission = "maintenance.manage";
    internal const string ComplianceReadPermission = "compliance.read";
    internal const string ComplianceManagePermission = "compliance.manage";
    internal const string DocumentsReadPermission = "documents.read";
    internal const string DocumentsManagePermission = "documents.manage";
    internal const string ReportsReadPermission = "reports.read";
    internal const string ReportsManagePermission = "reports.manage";

    internal event Action? Changed;

    internal Guid? UserId => Guid.TryParse(
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : null;

    internal string? Email => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    internal IReadOnlyList<string> Permissions => httpContextAccessor.HttpContext?.User
        .FindAll(PermissionClaimType)
        .Select(claim => claim.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList()
        ?? [];

    internal bool CanAccessAdministration => HasPermission(ReadUsersPermission) || HasPermission(ManageUsersPermission)
        || HasPermission(ReadRolesPermission) || HasPermission(ManageRolesPermission);

    internal bool CanManageAdministration => HasPermission(ManageUsersPermission)
        && HasPermission(ManageRolesPermission);

    internal bool CanAccessFleet => CanAccess(FleetReadPermission, FleetManagePermission);
    internal bool CanAccessCrew => CanAccess(CrewReadPermission, CrewManagePermission);
    internal bool CanAccessVoyages => CanAccess(VoyagesReadPermission, VoyagesManagePermission);
    internal bool CanAccessCargo => CanAccess(CargoReadPermission, CargoManagePermission);
    internal bool CanAccessMaintenance => CanAccess(MaintenanceReadPermission, MaintenanceManagePermission);
    internal bool CanAccessCompliance => CanAccess(ComplianceReadPermission, ComplianceManagePermission);
    internal bool CanAccessDocuments => CanAccess(DocumentsReadPermission, DocumentsManagePermission);
    internal bool CanAccessReports => CanAccess(ReportsReadPermission, ReportsManagePermission);

    internal bool HasPermission(string permission) => httpContextAccessor.HttpContext?.User.HasClaim(PermissionClaimType, permission) == true;

    private bool CanAccess(string readPermission, string managePermission) =>
        HasPermission(readPermission) || HasPermission(managePermission);

    internal void RefreshVisibleAuthorization() => Changed?.Invoke();
}
