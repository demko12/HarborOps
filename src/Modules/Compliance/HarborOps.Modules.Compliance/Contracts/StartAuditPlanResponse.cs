namespace HarborOps.Modules.Compliance.Contracts;

public sealed record StartAuditPlanResponse(
    Guid Id,
    string Status,
    DateTime StartedAtUtc);
