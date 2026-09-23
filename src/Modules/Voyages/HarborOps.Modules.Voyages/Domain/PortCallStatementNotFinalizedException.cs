namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatementNotFinalizedException(Guid portCallStatementId)
    : Exception($"Port-call statement '{portCallStatementId}' is still a draft and cannot be printed.")
{
    internal Guid PortCallStatementId { get; } = portCallStatementId;
}
