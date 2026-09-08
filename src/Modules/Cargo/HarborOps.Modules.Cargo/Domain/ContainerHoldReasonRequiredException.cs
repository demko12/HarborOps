namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ContainerHoldReasonRequiredException(Guid containerControlId)
    : Exception($"Container control '{containerControlId}' cannot be held without a reason.")
{
    internal Guid ContainerControlId { get; } = containerControlId;
}
