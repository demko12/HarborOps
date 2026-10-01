namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancySourceExcursionOpenException(
    Guid discrepancyId,
    Guid excursionId)
    : Exception(
        $"Discrepancy '{discrepancyId}' cannot be resolved while its temperature excursion '{excursionId}' is still open. Resolve the excursion or correct its reading first.")
{
    internal Guid DiscrepancyId { get; } = discrepancyId;

    internal Guid ExcursionId { get; } = excursionId;
}
