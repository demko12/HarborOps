using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class PortLookupValidator
{
    internal const string PortIdsField = "portIds";
    internal const int MaxPortIds = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(PortLookupRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var portIds = request.PortIds;

        if (portIds is null || portIds.Count == 0)
        {
            errors[PortIdsField] = ["At least one port ID is required."];

            return errors;
        }

        if (portIds.Any(portId => portId == Guid.Empty))
        {
            errors[PortIdsField] = ["Port IDs cannot be empty GUIDs."];

            return errors;
        }

        if (portIds.Distinct().Count() > MaxPortIds)
        {
            errors[PortIdsField] = [$"A maximum of {MaxPortIds} port ids is allowed."];
        }

        return errors;
    }
}
