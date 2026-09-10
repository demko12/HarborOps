namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ConvertDefectToWorkOrderResponse(Guid DefectId, Guid WorkOrderId);
