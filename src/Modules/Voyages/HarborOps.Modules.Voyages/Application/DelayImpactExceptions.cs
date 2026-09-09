namespace HarborOps.Modules.Voyages.Application;

public sealed class ApplyDelayImpactValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Apply delay impact validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class DelayNotFoundException(Guid delayId)
    : Exception($"Delay '{delayId}' was not found.")
{
    public Guid DelayId { get; } = delayId;
}

public sealed class DelayImpactAlreadyAppliedException(Guid delayId)
    : Exception($"Delay '{delayId}' already has an applied estimate revision.")
{
    public Guid DelayId { get; } = delayId;
}

public sealed class StaleDelayImpactPreviewException(Guid voyageStopId)
    : Exception(
        $"Voyage stop '{voyageStopId}' changed after the impact preview was produced.")
{
    public Guid VoyageStopId { get; } = voyageStopId;
}
