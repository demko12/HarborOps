namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetEquipmentResponse(
    IReadOnlyList<EquipmentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
