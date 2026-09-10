namespace HarborOps.Web.Components.Pages.Maintenance;

internal sealed record DefectRow(
    Guid DefectId,
    Guid VesselId,
    string VesselDisplayName,
    string Title,
    string Status,
    string EffectiveSeverity,
    DateTime ReportedAtUtc);
