namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselPositionHistoryRequest { public DateTime? FromUtc { get; init; } public DateTime? ToUtc { get; init; } public int? PageNumber { get; init; } public int? PageSize { get; init; } }
public sealed record VesselPositionHistoryResponse(IReadOnlyList<VesselPositionHistoryItem> Items, int PageNumber, int PageSize, int TotalCount);
public sealed record VesselPositionHistoryItem(Guid Id, decimal Latitude, decimal Longitude, DateTime ReportedAtUtc, Guid ReportedByUserId, string? ReportedByDisplayName);
public interface IVesselPositionHistoryQuery { Task<VesselPositionHistoryResponse> GetAsync(Guid vesselId, VesselPositionHistoryRequest request, CancellationToken cancellationToken); }
