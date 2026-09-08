using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class ContainerNotFoundException(Guid containerId)
    : Exception($"Container with ID '{containerId}' was not found.")
{
    internal Guid ContainerId { get; } = containerId;
}

internal sealed class ContainerControlValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Container control validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DuplicateActiveSealException(string sealNumber)
    : Exception($"Seal number '{sealNumber}' is already active on another container.")
{
    internal string SealNumber { get; } = sealNumber;
}

internal sealed class ContainerControlTransitionException(
    Guid containerId,
    ContainerControlState currentState,
    string requestedAction,
    string reason)
    : Exception(
        $"Container with ID '{containerId}' cannot be {requestedAction} while it is '{currentState}' because {reason}.")
{
    internal Guid ContainerId { get; } = containerId;

    internal ContainerControlState CurrentState { get; } = currentState;

    internal string RequestedAction { get; } = requestedAction;

    internal string Reason { get; } = reason;
}
