namespace HarborOps.Modules.Compliance.Contracts;

public sealed record CancelAuditPlanRequest(string? Reason);

public sealed record CancelAuditPlanResponse(
    Guid Id,
    string Status,
    DateTime CancelledAtUtc);
