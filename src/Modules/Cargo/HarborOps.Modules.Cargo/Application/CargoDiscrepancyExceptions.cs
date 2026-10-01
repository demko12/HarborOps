using HarborOps.Modules.Cargo.Contracts;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class CargoDiscrepancyValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cargo discrepancy validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CargoDiscrepancyNotFoundException(Guid discrepancyId)
    : Exception($"Cargo discrepancy with ID '{discrepancyId}' was not found.")
{
    internal Guid DiscrepancyId { get; } = discrepancyId;
}

internal sealed class ContainerHasNoBookingException(Guid containerId)
    : Exception($"Container with ID '{containerId}' is not assigned to a cargo booking.")
{
    internal Guid ContainerId { get; } = containerId;
}

internal sealed class CargoBookingCompletionBlockedException(CargoCompletionBlockers blockers)
    : Exception(
        $"Cargo booking with ID '{blockers.CargoBookingId}' cannot be completed while {blockers.OpenCount} discrepancies are reported or under investigation.")
{
    internal CargoCompletionBlockers Blockers { get; } = blockers;
}
