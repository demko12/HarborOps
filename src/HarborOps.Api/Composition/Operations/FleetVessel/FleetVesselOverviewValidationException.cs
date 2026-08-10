namespace HarborOps.Api.Composition.Operations.FleetVessel;

public sealed class FleetVesselOverviewValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Fleet vessel overview validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
