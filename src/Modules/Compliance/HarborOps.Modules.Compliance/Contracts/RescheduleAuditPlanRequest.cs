namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RescheduleAuditPlanRequest
{
    public DateOnly? PlannedStartDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public string? Reason { get; init; }
}

public sealed record RescheduleAuditPlanResponse(
    Guid Id,
    DateOnly PlannedStartDate,
    DateOnly DueDate);
