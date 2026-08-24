namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortLookupItem(
    Guid PortId,
    string Name,
    string Country,
    string? UnLocode);
