namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferReadingReasonRequiredException(
    Guid readingId,
    string requestedAction)
    : Exception($"Reading '{readingId}' cannot be {requestedAction} without a reason.")
{
    internal Guid ReadingId { get; } = readingId;

    internal string RequestedAction { get; } = requestedAction;
}
