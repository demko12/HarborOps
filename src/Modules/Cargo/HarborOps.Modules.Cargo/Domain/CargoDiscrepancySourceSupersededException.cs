namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancySourceSupersededException(
    Guid excursionId,
    Guid? supersededByExcursionId)
    : Exception(
        $"Temperature excursion '{excursionId}' was superseded by a corrected reading and can no longer be linked to a discrepancy.")
{
    internal Guid ExcursionId { get; } = excursionId;

    internal Guid? SupersededByExcursionId { get; } = supersededByExcursionId;
}
