namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferProfileChangeReasonRequiredException(Guid profileId)
    : Exception($"Reefer monitoring profile '{profileId}' cannot be revised without a reason.")
{
    internal Guid ProfileId { get; } = profileId;
}
