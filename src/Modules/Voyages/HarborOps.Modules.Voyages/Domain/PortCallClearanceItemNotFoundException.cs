namespace HarborOps.Modules.Voyages.Domain;

internal sealed class PortCallClearanceItemNotFoundException(Guid itemId)
    : Exception($"Port-call clearance item '{itemId}' was not found.")
{
    internal Guid ItemId { get; } = itemId;
}
