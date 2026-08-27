namespace HarborOps.Modules.Cargo.Domain;

internal sealed class LoadUnloadOperationTransitionNotAllowedException(
    Guid operationId,
    CargoOperationState currentState,
    string requestedAction)
    : Exception(
        $"Load or unload operation '{operationId}' cannot be {requestedAction} while it is '{currentState}'.")
{
    internal const string StartAction = "started";

    internal const string CompleteAction = "completed";

    internal Guid OperationId { get; } = operationId;

    internal CargoOperationState CurrentState { get; } = currentState;

    internal string RequestedAction { get; } = requestedAction;
}
