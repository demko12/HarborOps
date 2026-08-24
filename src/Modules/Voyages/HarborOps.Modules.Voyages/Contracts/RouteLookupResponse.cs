namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RouteLookupResponse(IReadOnlyList<RouteLookupItem> Items);
