namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ContainerTemperatureNotAllowedException(Guid containerControlId)
    : Exception(
        $"Container control '{containerControlId}' cannot record a temperature because the container is not a reefer.")
{
    internal Guid ContainerControlId { get; } = containerControlId;
}
