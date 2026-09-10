namespace HarborOps.Modules.Compliance.Contracts;

public sealed record GetAuditPlansRequest
{
    public Guid? VesselId { get; init; }

    public string? Type { get; init; }

    public string? Status { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record AuditPlanListItem(
    Guid Id,
    Guid VesselId,
    string Type,
    string Status,
    DateOnly PlannedStartDate,
    DateOnly DueDate,
    Guid? AssignedInspectorUserId,
    bool IsOverdue);

public sealed record GetAuditPlansResponse(
    IReadOnlyList<AuditPlanListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
