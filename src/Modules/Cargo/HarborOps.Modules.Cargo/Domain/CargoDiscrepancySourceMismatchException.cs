namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancySourceMismatchException(
    Guid excursionId,
    Guid? containerId,
    CargoDiscrepancyType type)
    : Exception(
        $"Temperature excursion '{excursionId}' can only be linked to a TemperatureRelated discrepancy on its own container, not to a '{type}' discrepancy on container '{containerId}'.")
{
    internal Guid ExcursionId { get; } = excursionId;

    internal Guid? ContainerId { get; } = containerId;

    internal CargoDiscrepancyType Type { get; } = type;
}
