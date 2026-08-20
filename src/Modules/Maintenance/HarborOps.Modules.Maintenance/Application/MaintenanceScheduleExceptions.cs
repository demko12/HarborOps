namespace HarborOps.Modules.Maintenance.Application;

internal sealed class RegisterMaintenanceScheduleValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register maintenance schedule validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetMaintenanceSchedulesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get maintenance schedules validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class UpdateScheduleNextDueValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update maintenance schedule next due validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class MaintenanceScheduleNotFoundException(Guid scheduleId)
    : Exception($"Maintenance schedule '{scheduleId}' was not found.")
{
    internal Guid ScheduleId { get; } = scheduleId;
}

internal sealed class MaintenanceScheduleEquipmentNotFoundException(IReadOnlyList<Guid> equipmentIds)
    : Exception($"Equipment {string.Join(", ", equipmentIds)} was not found in the maintenance module.")
{
    internal IReadOnlyList<Guid> EquipmentIds { get; } = equipmentIds;
}

internal sealed class MaintenanceScheduleVesselConflictException(IReadOnlyList<Guid> vesselIds)
    : Exception($"A maintenance schedule must cover equipment of a single vessel, but {vesselIds.Count} vessels were referenced.")
{
    internal IReadOnlyList<Guid> VesselIds { get; } = vesselIds;
}
