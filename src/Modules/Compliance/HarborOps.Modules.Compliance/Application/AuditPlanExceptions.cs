namespace HarborOps.Modules.Compliance.Application;

internal sealed class AuditPlanNotFoundException(Guid auditPlanId)
    : Exception($"Audit plan '{auditPlanId}' was not found.")
{
    internal Guid AuditPlanId { get; } = auditPlanId;
}

internal sealed class OverlappingAuditPlanException(Guid vesselId, string type)
    : Exception($"Vessel '{vesselId}' already has an active audit plan of type '{type}' overlapping these dates.")
{
    internal Guid VesselId { get; } = vesselId;

    internal string Type { get; } = type;
}

internal sealed class RegisterAuditPlanValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register audit plan validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RescheduleAuditPlanValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Reschedule audit plan validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class AssignAuditPlanInspectorValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Assign audit plan inspector validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CompleteAuditPlanValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Complete audit plan validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CancelAuditPlanValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cancel audit plan validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
