using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class BillOfLadingNotFoundException(Guid billOfLadingId)
    : Exception($"Bill of lading with ID '{billOfLadingId}' was not found.")
{
    internal Guid BillOfLadingId { get; } = billOfLadingId;
}

internal sealed class BillOfLadingValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Bill of lading validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DuplicateBillNumberException(string billNumber)
    : Exception($"Bill number '{billNumber}' is already used by another bill of lading.")
{
    internal string BillNumber { get; } = billNumber;
}

internal sealed class ContainerNotInBookingException(
    Guid cargoBookingId,
    IReadOnlyList<Guid> containerIds)
    : Exception(
        $"These containers do not belong to cargo booking '{cargoBookingId}': {string.Join(", ", containerIds)}.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal IReadOnlyList<Guid> ContainerIds { get; } = containerIds;
}

internal sealed class ContainerAlreadyOnBillException(IReadOnlyList<string> containerNumbers)
    : Exception(
        $"These containers are already on another bill of lading that is not voided: {string.Join(", ", containerNumbers)}.")
{
    internal IReadOnlyList<string> ContainerNumbers { get; } = containerNumbers;
}

internal sealed class BillOfLadingTransitionException(
    Guid billOfLadingId,
    BillOfLadingStatus currentStatus,
    string requestedAction,
    string reason)
    : Exception(
        $"Bill of lading with ID '{billOfLadingId}' cannot be {requestedAction} while it is '{currentStatus}' because {reason}.")
{
    internal Guid BillOfLadingId { get; } = billOfLadingId;

    internal BillOfLadingStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;

    internal string Reason { get; } = reason;
}
