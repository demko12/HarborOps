namespace HarborOps.Modules.Compliance.Application;

internal sealed class PscInspectionNotFoundException(int pscId)
    : Exception($"Psc inspection '{pscId}' was not found.")
{
    internal int PscId { get; } = pscId;
}
