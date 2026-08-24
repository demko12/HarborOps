namespace HarborOps.Modules.Fleet.Contracts;

public sealed record SearchFleetsRequest(string? Search, int PageNumber = 1, int PageSize = 25);
public sealed record FleetListItem(Guid FleetId, string Name, string Description, DateTime CreatedAtUtc, int ActiveVesselCount);
public sealed record SearchFleetsResponse(IReadOnlyList<FleetListItem> Items, int TotalCount, int PageNumber, int PageSize);
public sealed record CreateFleetRequest(string Name, string Description);
public sealed record CreateFleetResponse(Guid FleetId);
public sealed record FleetVesselItem(Guid VesselId, string Name, string ImoNumber, string CurrentStatus, DateTime AssignedFromUtc);
public sealed record FleetDetailsResponse(Guid FleetId, string Name, string Description, DateTime CreatedAtUtc, IReadOnlyList<FleetVesselItem> ActiveVessels);
public sealed record FleetAssignmentHistoryItem(Guid FleetId, string FleetName, DateTime AssignedFromUtc, DateTime? AssignedToUtc, bool IsActive);
public sealed record FleetAssignmentHistoryResponse(IReadOnlyList<FleetAssignmentHistoryItem> Items);
public sealed record AssignFleetRequest(Guid FleetId);
public sealed record AssignFleetResponse(Guid AssignmentId);

public interface IFleetManagementQuery
{
    Task<SearchFleetsResponse> SearchAsync(SearchFleetsRequest request, CancellationToken cancellationToken);
    Task<FleetDetailsResponse?> GetDetailsAsync(Guid fleetId, CancellationToken cancellationToken);
    Task<FleetAssignmentHistoryResponse?> GetAssignmentHistoryAsync(Guid vesselId, CancellationToken cancellationToken);
}
