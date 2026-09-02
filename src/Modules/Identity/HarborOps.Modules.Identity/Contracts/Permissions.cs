namespace HarborOps.Modules.Identity.Contracts;

public static class Permissions
{
    public const string ClaimType = "harborops:permission";

    public static class Fleet
    {
        public const string Read = "fleet.read";
        public const string Manage = "fleet.manage";
    }

    public static class Crew
    {
        public const string Read = "crew.read";
        public const string Manage = "crew.manage";
    }

    public static class Voyages
    {
        public const string Read = "voyages.read";
        public const string Manage = "voyages.manage";
    }

    public static class Cargo
    {
        public const string Read = "cargo.read";
        public const string Manage = "cargo.manage";
    }

    public static class Maintenance
    {
        public const string Read = "maintenance.read";
        public const string Manage = "maintenance.manage";
    }

    public static class Compliance
    {
        public const string Read = "compliance.read";
        public const string Manage = "compliance.manage";
    }

    public static class Documents
    {
        public const string Read = "documents.read";
        public const string Manage = "documents.manage";
    }

    public static class Reports
    {
        public const string Read = "reports.read";
        public const string Manage = "reports.manage";
    }

    public static class Notifications
    {
        public const string Read = "notifications.read";
        public const string Manage = "notifications.manage";
    }

    public static class IdentityUsers
    {
        public const string Read = "identity.users.read";
        public const string Manage = "identity.users.manage";
    }

    public static class IdentityRoles
    {
        public const string Read = "identity.roles.read";
        public const string Manage = "identity.roles.manage";
    }

    /// <summary>
    /// The fixed permission catalog. Extend this list, and only this list, when a
    /// new capability needs read/manage administration.
    /// </summary>
    public static IReadOnlyList<PermissionDescriptor> Catalog { get; } =
    [
        new(Fleet.Read, "fleet", "read"),
        new(Fleet.Manage, "fleet", "manage"),
        new(Crew.Read, "crew", "read"),
        new(Crew.Manage, "crew", "manage"),
        new(Voyages.Read, "voyages", "read"),
        new(Voyages.Manage, "voyages", "manage"),
        new(Cargo.Read, "cargo", "read"),
        new(Cargo.Manage, "cargo", "manage"),
        new(Maintenance.Read, "maintenance", "read"),
        new(Maintenance.Manage, "maintenance", "manage"),
        new(Compliance.Read, "compliance", "read"),
        new(Compliance.Manage, "compliance", "manage"),
        new(Documents.Read, "documents", "read"),
        new(Documents.Manage, "documents", "manage"),
        new(Reports.Read, "reports", "read"),
        new(Reports.Manage, "reports", "manage"),
        new(Notifications.Read, "notifications", "read"),
        new(Notifications.Manage, "notifications", "manage"),
        new(IdentityUsers.Read, "identity.users", "read"),
        new(IdentityUsers.Manage, "identity.users", "manage"),
        new(IdentityRoles.Read, "identity.roles", "read"),
        new(IdentityRoles.Manage, "identity.roles", "manage"),
    ];
}

/// <summary>
/// A single entry in the permission catalog. <see cref="Capability"/> is the
/// dotted resource key (e.g. "fleet", "identity.users") and <see cref="Action"/>
/// is either "read" or "manage". <see cref="Name"/> is the full claim value and
/// authorization policy name (e.g. "fleet.manage").
/// </summary>
public sealed record PermissionDescriptor(string Name, string Capability, string Action);
