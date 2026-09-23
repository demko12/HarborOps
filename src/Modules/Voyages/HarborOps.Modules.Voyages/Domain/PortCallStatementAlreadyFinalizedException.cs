namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallStatementAlreadyFinalizedException(Guid portCallStatementId)
    : Exception($"Port-call statement '{portCallStatementId}' is finalized and can no longer be changed.")
{
    internal Guid PortCallStatementId { get; } = portCallStatementId;
}
