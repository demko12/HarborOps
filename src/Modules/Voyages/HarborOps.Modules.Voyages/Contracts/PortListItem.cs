namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortListItem(
    Guid PortId,
    string Name,
    string Country,
    string? City,
    string? UnLocode,
    decimal? Latitude,
    decimal? Longitude);
