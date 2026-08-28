namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RecordDelayResponse(
    Guid DelayId,
    Guid VoyageId,
    Guid? VoyageStopId,
    string Reason,
    string? Description,
    int Duration,
    DateTime CreatedAt);
