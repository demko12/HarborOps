using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.Modules.Fleet.Application;

internal static class VesselLookupValidator
{
    internal const string VesselIdsField = "vesselIds";
    internal const int MaxVesselIds = 100;
    internal static IReadOnlyDictionary<string, string[]> Validate(VesselLookupRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var vesselIds = request.VesselIds;

        if (vesselIds is null || vesselIds.Count == 0)
        {
            errors[VesselIdsField] = ["At least one vessel ID is required."];
            return errors;

        }

        if (vesselIds.Any(vesselId => vesselId == Guid.Empty))
        {
            errors[VesselIdsField] = ["Vessel IDs cannot be empty GUIDs."];
            return errors;
        }

        if (vesselIds.Distinct().Count() > MaxVesselIds)
        {
            errors[VesselIdsField] = [$"A maximum of {MaxVesselIds} vessel ids is allowed."];
        }

        return errors;

    }
}
