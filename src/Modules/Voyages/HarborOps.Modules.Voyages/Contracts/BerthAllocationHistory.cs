namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthAllocationHistoryItem(Guid ChangeId, Guid? PreviousBerthId, string? PreviousBerthName, Guid NewBerthId, string NewBerthName, DateTime PreviousPlannedArrivalUtc, DateTime PreviousPlannedDepartureUtc, DateTime NewPlannedArrivalUtc, DateTime NewPlannedDepartureUtc, string Reason, Guid ChangedByUserId, DateTime ChangedAtUtc);
public sealed record BerthAllocationHistoryResponse(Guid VoyageId, Guid StopId, IReadOnlyList<BerthAllocationHistoryItem> Changes);
public interface IGetBerthAllocationHistoryQuery { Task<BerthAllocationHistoryResponse> GetAsync(Guid voyageId, Guid stopId, CancellationToken cancellationToken); }
