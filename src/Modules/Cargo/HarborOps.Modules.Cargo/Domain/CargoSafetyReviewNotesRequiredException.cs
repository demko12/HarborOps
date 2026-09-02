namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoSafetyReviewNotesRequiredException(Guid cargoSafetyInfoId)
    : Exception(
        $"Cargo safety information '{cargoSafetyInfoId}' cannot be rejected without review notes.")
{
    internal Guid CargoSafetyInfoId { get; } = cargoSafetyInfoId;
}
