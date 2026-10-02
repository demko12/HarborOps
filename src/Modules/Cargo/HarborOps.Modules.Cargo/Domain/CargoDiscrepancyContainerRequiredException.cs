namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancyContainerRequiredException(CargoDiscrepancyType type)
    : Exception($"A '{type}' discrepancy must name the container it was observed on.")
{
    internal CargoDiscrepancyType Type { get; } = type;
}
