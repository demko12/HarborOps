namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record DefectSummary(
    Guid Id,
    Guid VesselId,
    Guid? EquipmentId,
    string Title,
    string ReportedSeverity,
    string? ConfirmedSeverity,
    string? Priority,
    string Status,
    DateTime ObservedAtUtc,
    DateTime ReportedAtUtc);
