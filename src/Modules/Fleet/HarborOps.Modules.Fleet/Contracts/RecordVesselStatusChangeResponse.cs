namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RecordVesselStatusChangeResponse(
    Guid Id,
    Guid VesselId,
    string Status,
    string ChangedBy,
    string? Notes,
    DateTime ChangedAtUtc);
