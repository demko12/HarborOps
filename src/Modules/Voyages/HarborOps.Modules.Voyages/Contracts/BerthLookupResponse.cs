namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthLookupResponse(IReadOnlyList<BerthLookupItem> Items);
