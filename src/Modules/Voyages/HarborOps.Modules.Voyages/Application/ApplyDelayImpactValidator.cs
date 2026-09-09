using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class ApplyDelayImpactValidator
{
    internal const string ReasonField = "reason";
    internal const string StopsField = "stops";

    internal static IReadOnlyDictionary<string, string[]> Validate(ApplyDelayImpactRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Revision reason is required."];
        }
        else if (request.Reason.Trim().Length > VoyageEstimateRevisionBatch.ReasonMaxLength)
        {
            errors[ReasonField] =
            [
                $"Revision reason must not exceed {VoyageEstimateRevisionBatch.ReasonMaxLength} characters.",
            ];
        }

        if (request.Stops is null || request.Stops.Count == 0)
        {
            errors[StopsField] = ["At least one revised stop estimate is required."];

            return errors;
        }

        if (request.Stops.Any(stop => stop.VoyageStopId is null || stop.VoyageStopId == Guid.Empty))
        {
            errors[StopsField] = ["Every revised stop requires a voyage stop identifier."];
        }
        else if (request.Stops.Any(stop => stop.ExpectedEta is null
            || stop.ExpectedEtd is null
            || stop.Eta is null
            || stop.Etd is null))
        {
            errors[StopsField] =
            [
                "Every revised stop requires the expected and the proposed ETA and ETD.",
            ];
        }
        else if (request.Stops.Select(stop => stop.VoyageStopId!.Value).Distinct().Count()
            != request.Stops.Count)
        {
            errors[StopsField] = ["Every voyage stop may only be revised once per request."];
        }

        return errors;
    }
}
