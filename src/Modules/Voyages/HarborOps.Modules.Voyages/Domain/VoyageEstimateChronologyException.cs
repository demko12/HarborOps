using System.Globalization;

namespace HarborOps.Modules.Voyages.Domain;

public sealed class VoyageEstimateChronologyException(
    Guid voyageStopId,
    DateTime estimateUtc,
    DateTime earliestAllowedUtc)
    : Exception(
        string.Format(
            CultureInfo.InvariantCulture,
            "Voyage stop '{0}' estimate '{1:O}' must not precede '{2:O}'.",
            voyageStopId,
            estimateUtc,
            earliestAllowedUtc))
{
    public Guid VoyageStopId { get; } = voyageStopId;

    public DateTime EstimateUtc { get; } = estimateUtc;

    public DateTime EarliestAllowedUtc { get; } = earliestAllowedUtc;
}
