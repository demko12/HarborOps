using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.Modules.Compliance.Application;

internal static class GetVesselComplianceOverviewValidator
{
    internal const string VesselIdField = "vesselId";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        GetVesselComplianceOverviewRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel is required."];
        }

        return errors;
    }
}
