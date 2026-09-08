namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthAvailabilityItem(Guid BerthId, string BerthName, decimal Length, decimal MaxDraft, bool IsAvailableForRequestedWindow, DateTime? NearestAvailableFromUtc, DateTime? NearestAvailableToUtc);
public sealed record BerthAvailabilityResponse(Guid PortId, DateTime FromUtc, DateTime ToUtc, DateTime SearchHorizonEndsUtc, IReadOnlyList<BerthAvailabilityItem> Berths);
