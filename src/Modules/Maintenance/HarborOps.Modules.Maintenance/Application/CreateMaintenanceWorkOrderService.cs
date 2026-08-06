using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CreateMaintenanceWorkOrderService(DbContext dbContext)
{
    public async Task<CreateMaintenanceWorkOrderResponse> CreateAsync(
        CreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CreateMaintenanceWorkOrderValidator.Validate(request);
        if(errors.Count > 0)
        {
            throw new CreateMaintenanceWorkOrderValidationException(errors);   
        }

        var vesselId = request.VesselId!.Value;
        var title = request.Title!.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var priority = Enum.Parse<WorkOrderPriority>(request.Priority!, ignoreCase: true);
        var dueUtc = request.DueUtc!.Value;

        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var workOrder = MaintenanceWorkOrder.Create(vesselId, title, description, priority, dueUtc);

        await workOrders.AddAsync(workOrder, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateMaintenanceWorkOrderResponse(workOrder.Id);
    }
}
