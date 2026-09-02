using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class CargoBookingCompletedException(Guid cargoBookingId)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' is completed and cannot receive containers or operations.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class DuplicateContainerNumberException(string containerNumber)
    : Exception($"A container with number '{containerNumber}' is already registered.")
{
    internal string ContainerNumber { get; } = containerNumber;
}

internal sealed class AddCargoContainerValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Add cargo container validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PlanCargoOperationValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Plan cargo operation validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CargoOperationNotFoundException(Guid operationId)
    : Exception($"Cargo operation with ID '{operationId}' was not found.")
{
    internal Guid OperationId { get; } = operationId;
}

internal sealed class CargoOperationTransitionException(
    Guid operationId,
    CargoOperationState currentState,
    string requestedAction)
    : Exception(
        $"Cargo operation with ID '{operationId}' cannot be {requestedAction} while it is '{currentState}'.")
{
    internal Guid OperationId { get; } = operationId;

    internal CargoOperationState CurrentState { get; } = currentState;

    internal string RequestedAction { get; } = requestedAction;
}

internal sealed class CargoOperationAlreadyInProgressException(Guid cargoBookingId)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' already has an operation in progress.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class CargoManifestEmptyException(Guid cargoBookingId)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' has no containers, so an operation cannot be started.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class CargoManifestNotReadyException(
    Guid cargoBookingId,
    CargoOperationType operationType,
    CargoStatus requiredContainerStatus)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' cannot start a '{operationType}' operation because not every container is '{requiredContainerStatus}'.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal CargoOperationType OperationType { get; } = operationType;

    internal CargoStatus RequiredContainerStatus { get; } = requiredContainerStatus;
}

internal sealed class CargoOperationBookingPhaseException(
    Guid cargoBookingId,
    CargoOperationType operationType,
    CargoStatus currentStatus,
    CargoStatus requiredStatus)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' is '{currentStatus}', so a '{operationType}' operation can only start while it is '{requiredStatus}'.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal CargoOperationType OperationType { get; } = operationType;

    internal CargoStatus CurrentStatus { get; } = currentStatus;

    internal CargoStatus RequiredStatus { get; } = requiredStatus;
}
