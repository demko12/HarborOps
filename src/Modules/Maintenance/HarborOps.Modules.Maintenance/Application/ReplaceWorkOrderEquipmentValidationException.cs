using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class ReplaceWorkOrderEquipmentValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Replace work order equipment validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class WorkOrderEquipmentNotFoundException(IReadOnlyList<Guid> equipmentIds)
    : Exception($"Equipment {string.Join(", ", equipmentIds)} was not found in the maintenance module.")
{
    internal IReadOnlyList<Guid> EquipmentIds { get; } = equipmentIds;
}

internal sealed class WorkOrderEquipmentVesselMismatchException(
    Guid workOrderId,
    Guid vesselId,
    IReadOnlyList<Guid> equipmentIds)
    : Exception(
        $"Equipment {string.Join(", ", equipmentIds)} does not belong to vessel '{vesselId}' of work order '{workOrderId}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal Guid VesselId { get; } = vesselId;

    internal IReadOnlyList<Guid> EquipmentIds { get; } = equipmentIds;
}
