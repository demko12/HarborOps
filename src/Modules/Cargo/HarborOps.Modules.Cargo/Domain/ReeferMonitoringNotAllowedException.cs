namespace HarborOps.Modules.Cargo.Domain;

internal sealed class ReeferMonitoringNotAllowedException(Guid containerId)
    : Exception(
        $"Container '{containerId}' cannot be temperature monitored because it is not a reefer.")
{
    internal Guid ContainerId { get; } = containerId;
}
