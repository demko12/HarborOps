namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferThresholdsNotValidException(Guid containerId, string reason)
    : Exception($"Container '{containerId}' cannot be monitored because {reason}.")
{
    internal const string MinimumAboveMaximumReason =
        "the minimum temperature is above the maximum temperature";
    internal const string TargetOutOfRangeReason =
        "the target temperature is outside the acceptable range";
    internal const string OutOfSupportedRangeReason =
        "a threshold is outside the supported temperature range";
    internal const string PrecisionNotSupportedReason =
        "a threshold has more decimal places than are supported";

    internal Guid ContainerId { get; } = containerId;

    internal string Reason { get; } = reason;
}
