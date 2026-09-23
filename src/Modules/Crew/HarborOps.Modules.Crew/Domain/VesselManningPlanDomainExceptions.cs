namespace HarborOps.Modules.Crew.Domain;

internal sealed class InvalidVesselManningPlanEndDateException(
    Guid vesselManningPlanId,
    DateOnly requestedEndDate,
    DateOnly effectiveStartDate,
    DateOnly currentEndDate)
    : Exception(
        $"New end date {requestedEndDate:yyyy-MM-dd} for manning plan '{vesselManningPlanId}' must be on or after "
        + $"the start date {effectiveStartDate:yyyy-MM-dd} and on or before the current end date {currentEndDate:yyyy-MM-dd}.")
{
    internal Guid VesselManningPlanId { get; } = vesselManningPlanId;
    internal DateOnly RequestedEndDate { get; } = requestedEndDate;
    internal DateOnly EffectiveStartDate { get; } = effectiveStartDate;
    internal DateOnly CurrentEndDate { get; } = currentEndDate;
}
