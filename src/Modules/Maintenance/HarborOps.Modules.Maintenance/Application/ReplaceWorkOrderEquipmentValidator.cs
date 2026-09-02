using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class ReplaceWorkOrderEquipmentValidator
{
    internal const string EquipmentIdsField = "equipmentIds";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        ReplaceWorkOrderEquipmentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.EquipmentIds is null)
        {
            errors[EquipmentIdsField] = ["Required."];

            return errors;
        }

        if (request.EquipmentIds.Any(equipmentId => equipmentId == Guid.Empty))
        {
            errors[EquipmentIdsField] = ["Must not contain empty values."];

            return errors;
        }

        if (request.EquipmentIds.Distinct().Count() != request.EquipmentIds.Count)
        {
            errors[EquipmentIdsField] = ["Must not contain duplicates."];
        }

        return errors;
    }
}
