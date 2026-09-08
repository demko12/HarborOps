namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoSafetyInfoTransitionNotAllowedException(
    Guid cargoSafetyInfoId,
    CargoSafetyReviewStatus currentStatus,
    string requestedAction)
    : Exception(
        $"Cargo safety information '{cargoSafetyInfoId}' cannot be {requestedAction} while it is '{currentStatus}'.")
{
    internal const string EditAction = "edited";
    internal const string ReviewAction = "reviewed";
    internal const string SubmitAction = "submitted";

    internal Guid CargoSafetyInfoId { get; } = cargoSafetyInfoId;

    internal CargoSafetyReviewStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;
}
