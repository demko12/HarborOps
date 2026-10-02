namespace HarborOps.Modules.Fleet.Contracts;

public sealed record AisTrackingStatus(string ConnectionState, DateTime? LastPositionReceivedAtUtc);
