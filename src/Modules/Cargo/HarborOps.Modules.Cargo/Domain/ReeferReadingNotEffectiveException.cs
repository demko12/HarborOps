namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferReadingNotEffectiveException(
    Guid readingId,
    ReeferReadingStatus currentStatus,
    string requestedAction)
    : Exception(
        $"Reading '{readingId}' cannot be {requestedAction} while it is '{currentStatus}'.")
{
    internal const string CorrectAction = "corrected";
    internal const string VoidAction = "voided";

    internal Guid ReadingId { get; } = readingId;

    internal ReeferReadingStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;
}
