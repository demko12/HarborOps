namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferReadingNotValidException(Guid containerId, string reason)
    : Exception($"Container '{containerId}' cannot record the reading because {reason}.")
{
    internal const string MeasuredInFutureReason =
        "it was measured in the future";
    internal const string OutOfSupportedRangeReason =
        "the temperature is outside the supported temperature range";
    internal const string PrecisionNotSupportedReason =
        "the temperature has more decimal places than are supported";

    internal Guid ContainerId { get; } = containerId;

    internal string Reason { get; } = reason;
}
