using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class AssignBerthValidator
{
    internal const string BerthIdField = "berthId";

    internal static IReadOnlyDictionary<string, string[]> Validate(AssignBerthToStopRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.BerthId is null || request.BerthId == Guid.Empty)
        {
            errors[BerthIdField] = ["Berth is required."];
        }

        return errors;
    }
}
