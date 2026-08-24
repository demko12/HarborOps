namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortLookupResponse(IReadOnlyList<PortLookupItem> Items);
