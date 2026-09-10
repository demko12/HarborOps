namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterAuditPlanRequest
{
    public Guid? VesselId { get; init; }

    public string? Type { get; init; }

    public DateOnly? PlannedStartDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public Guid? AssignedInspectorUserId { get; init; }

    public string? Scope { get; init; }

    public string? Notes { get; init; }
}

public sealed record RegisterAuditPlanResponse(Guid Id);
