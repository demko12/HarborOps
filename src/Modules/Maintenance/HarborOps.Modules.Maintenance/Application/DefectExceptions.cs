namespace HarborOps.Modules.Maintenance.Application;

internal sealed class RegisterDefectValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register defect validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class TriageDefectValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Triage defect validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RejectDefectValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Reject defect validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class ConvertDefectToWorkOrderValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Convert defect to work order validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetDefectsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get defects validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DefectNotFoundException(Guid defectId)
    : Exception($"Defect '{defectId}' was not found.")
{
    internal Guid DefectId { get; } = defectId;
}

internal sealed class DefectVesselNotFoundException(Guid vesselId)
    : Exception($"Vessel '{vesselId}' was not found.")
{
    internal Guid VesselId { get; } = vesselId;
}

internal sealed class DefectEquipmentNotFoundException(Guid equipmentId)
    : Exception($"Equipment '{equipmentId}' was not found.")
{
    internal Guid EquipmentId { get; } = equipmentId;
}

internal sealed class DefectEquipmentVesselMismatchException(Guid equipmentId, Guid vesselId)
    : Exception($"Equipment '{equipmentId}' does not belong to vessel '{vesselId}'.")
{
    internal Guid EquipmentId { get; } = equipmentId;

    internal Guid VesselId { get; } = vesselId;
}
