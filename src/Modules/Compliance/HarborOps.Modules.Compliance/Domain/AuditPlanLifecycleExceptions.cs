namespace HarborOps.Modules.Compliance.Domain;

internal sealed class InvalidAuditPlanTransitionException(
    Guid auditPlanId,
    string currentStatus,
    string attemptedAction)
    : Exception(
        $"Audit plan '{auditPlanId}' in status '{currentStatus}' cannot {attemptedAction}.")
{
    internal Guid AuditPlanId { get; } = auditPlanId;

    internal string CurrentStatus { get; } = currentStatus;

    internal string AttemptedAction { get; } = attemptedAction;
}
