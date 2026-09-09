namespace HarborOps.Modules.Compliance.Contracts;

public sealed record AssignAuditPlanInspectorRequest
{
    public Guid? InspectorUserId { get; init; }

    public string? Reason { get; init; }
}

public sealed record AssignAuditPlanInspectorResponse(
    Guid Id,
    Guid InspectorUserId);
