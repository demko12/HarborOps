namespace HarborOps.Modules.Compliance.Contracts;

public sealed record AuditPlanHistoryItem(
    Guid Id,
    string EventType,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    string PreviousStatus,
    string NewStatus,
    DateOnly? PreviousPlannedStartDate,
    DateOnly? NewPlannedStartDate,
    DateOnly? PreviousDueDate,
    DateOnly? NewDueDate,
    Guid? PreviousInspectorUserId,
    Guid? NewInspectorUserId);

public sealed record GetAuditPlanDetailResponse(
    Guid Id,
    Guid VesselId,
    string Type,
    string Status,
    DateOnly PlannedStartDate,
    DateOnly DueDate,
    Guid? AssignedInspectorUserId,
    string Scope,
    string? Notes,
    bool IsOverdue,
    int? SurveyAuditId,
    IReadOnlyList<AuditPlanHistoryItem> History);
