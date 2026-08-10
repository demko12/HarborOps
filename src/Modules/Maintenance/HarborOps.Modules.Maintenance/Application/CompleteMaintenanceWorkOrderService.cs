using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CompleteMaintenanceWorkOrderService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        Guid workOrderId,
        CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        var currentUtc = timeProvider.GetUtcNow().UtcDateTime;
        var errors = CompleteMaintenanceWorkOrderValidator.Validate(request, currentUtc, workOrder.CreatedUtc);
        if (errors.Count > 0)
        {
            throw new CompleteMaintenanceWorkOrderValidationException(errors);
        }

        var technicianId = request.TechnicianId!.Value;
        var completedUtc = request.CompletedUtc!.Value;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        try
        {
            workOrder.Complete(completedUtc);
        }
        catch (WorkOrderNotCompletableException exception)
        {
            throw new CompleteMaintenanceWorkOrderConflictException(exception.WorkOrderId, exception.CurrentStatus);
        }

        var maintenanceRecord = MaintenanceRecord.Create
            (workOrder.Id,
            technicianId,
            DateOnly.FromDateTime(completedUtc),
            notes,
            request.Cost,
            request.LaborHours);

        await dbContext.Set<MaintenanceRecord>().AddAsync(maintenanceRecord, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompleteMaintenanceWorkOrderResponse(
            workOrder.Id,
            maintenanceRecord.Id,
            workOrder.CompletedUtc!.Value);
    }
}
