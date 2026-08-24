namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthListItem(
    Guid BerthId,
    Guid PortId,
    string Name,
    decimal Length,
    decimal MaxDraft,
    string Status);
