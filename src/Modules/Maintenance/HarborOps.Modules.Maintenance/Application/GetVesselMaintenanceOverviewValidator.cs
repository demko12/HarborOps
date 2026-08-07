using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetVesselMaintenanceOverviewValidator
{
    internal const string VesselIdsField = "vesselIds";
    internal const int MaxVesselIds = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(
        VesselMaintenanceOverviewRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var vesselIds = request.VesselIds;

        if (vesselIds is null || vesselIds.Count == 0)
        {
            errors[VesselIdsField] = ["At least one vessel id is required. "];
            return errors;
        }

        if (vesselIds.Any(vesselId => vesselId == Guid.Empty))
        {
            errors[VesselIdsField] = ["Vessel ids cannot be empty. "];
            return errors;
        }

        if (vesselIds.Distinct().Count() > MaxVesselIds)
        {
            errors[VesselIdsField] = [$"A maximum of {MaxVesselIds} vessel ids is allowed. "];
            return errors;
        }

        return errors;
    }
}
