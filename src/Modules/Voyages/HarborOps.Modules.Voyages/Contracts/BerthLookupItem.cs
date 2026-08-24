namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthLookupItem(
    Guid BerthId,
    Guid PortId,
    string Name,
    string Status);
