using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class ReeferMonitoringValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Reefer monitoring validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class ContainerIsNotReeferException(Guid containerId)
    : Exception($"Container with ID '{containerId}' is not a reefer.")
{
    internal Guid ContainerId { get; } = containerId;
}

internal sealed class ReeferProfileNotFoundException(Guid containerId)
    : Exception($"Container with ID '{containerId}' has no reefer monitoring profile.")
{
    internal Guid ContainerId { get; } = containerId;
}

internal sealed class ReeferReadingNotFoundException(Guid readingId)
    : Exception($"Reefer temperature reading with ID '{readingId}' was not found.")
{
    internal Guid ReadingId { get; } = readingId;
}

internal sealed class ReeferExcursionNotFoundException(Guid excursionId)
    : Exception($"Temperature excursion with ID '{excursionId}' was not found.")
{
    internal Guid ExcursionId { get; } = excursionId;
}

internal sealed class ReeferReadingTransitionException(
    Guid readingId,
    ReeferReadingStatus currentStatus,
    string requestedAction)
    : Exception(
        $"Reading '{readingId}' cannot be {requestedAction} while it is '{currentStatus}'.")
{
    internal Guid ReadingId { get; } = readingId;

    internal ReeferReadingStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;
}

internal sealed class ReeferExcursionTransitionException(
    Guid excursionId,
    ReeferExcursionStatus currentStatus,
    string requestedAction)
    : Exception(
        $"Temperature excursion '{excursionId}' cannot be {requestedAction} while it is '{currentStatus}'.")
{
    internal Guid ExcursionId { get; } = excursionId;

    internal ReeferExcursionStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;
}
