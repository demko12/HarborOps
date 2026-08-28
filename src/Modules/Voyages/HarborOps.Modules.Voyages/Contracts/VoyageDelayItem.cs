namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageDelayItem(
    Guid DelayId,
    Guid? VoyageStopId,
    string Reason,
    string? Description,
    int Duration,
    DateTime CreatedAt);
