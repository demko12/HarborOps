namespace HarborOps.Modules.Voyages.Contracts;

public sealed record ReplaceBerthAllocationRequest(Guid? BerthId, DateTime? PlannedArrivalUtc, DateTime? PlannedDepartureUtc, string? Reason);
public sealed record ReplaceBerthAllocationResponse(Guid StopId, Guid BerthId, DateTime PlannedArrivalUtc, DateTime PlannedDepartureUtc, DateTime ChangedAtUtc);
