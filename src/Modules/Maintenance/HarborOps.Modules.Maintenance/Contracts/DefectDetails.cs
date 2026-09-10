namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record DefectDetails(
    Guid Id,
    Guid VesselId,
    Guid? EquipmentId,
    string Title,
    string? Description,
    string ReportedSeverity,
    DateTime ObservedAtUtc,
    string Status,
    Guid ReportedByUserId,
    DateTime ReportedAtUtc,
    string? ConfirmedSeverity,
    string? Priority,
    string? TriageNotes,
    Guid? TriagedByUserId,
    DateTime? TriagedAtUtc,
    string? RejectionReason,
    Guid? RejectedByUserId,
    DateTime? RejectedAtUtc,
    Guid? WorkOrderId,
    Guid? ConvertedByUserId,
    DateTime? ConvertedAtUtc);
