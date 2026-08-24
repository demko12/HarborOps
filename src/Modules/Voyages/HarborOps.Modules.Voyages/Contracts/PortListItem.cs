namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortListItem(
    Guid PortId,
    string Name,
    string? City,
    string Country,
    string? UnLocode,
    string? ShipyardName);
