using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class ReplaceWorkOrderEquipmentService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ReplaceWorkOrderEquipmentResponse> ReplaceEquipmentAsync(
        Guid workOrderId,
        ReplaceWorkOrderEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ReplaceWorkOrderEquipmentValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ReplaceWorkOrderEquipmentValidationException(errors);
        }

        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        workOrder.EnsureEquipmentCanChange();

        var equipmentIds = request.EquipmentIds!.ToList();

        var equipment = await dbContext.Set<Equipment>()
            .AsNoTracking()
            .Where(item => equipmentIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new WorkOrderEquipmentListItem(
                item.Id,
                item.VesselId,
                item.Name,
                item.Category,
                item.Manufacturer,
                item.SerialNumber))
            .ToListAsync(cancellationToken);

        var missingEquipmentIds = equipmentIds
            .Except(equipment.Select(item => item.EquipmentId))
            .ToList();

        if (missingEquipmentIds.Count > 0)
        {
            throw new WorkOrderEquipmentNotFoundException(missingEquipmentIds);
        }

        var mismatchedEquipmentIds = equipment
            .Where(item => item.VesselId != workOrder.VesselId)
            .Select(item => item.EquipmentId)
            .ToList();

        if (mismatchedEquipmentIds.Count > 0)
        {
            throw new WorkOrderEquipmentVesselMismatchException(
                workOrder.Id,
                workOrder.VesselId,
                mismatchedEquipmentIds);
        }

        var linkSet = dbContext.Set<WorkOrderEquipment>();

        var existingLinks = await linkSet
            .Where(link => link.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);

        foreach (var removedLink in existingLinks.Where(link => !equipmentIds.Contains(link.EquipmentId)))
        {
            linkSet.Remove(removedLink);
        }

        foreach (var addedEquipmentId in equipmentIds
            .Where(equipmentId => existingLinks.TrueForAll(link => link.EquipmentId != equipmentId)))
        {
            linkSet.Add(WorkOrderEquipment.Create(workOrderId, addedEquipmentId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReplaceWorkOrderEquipmentResponse(
            workOrder.Id,
            workOrder.Status.ToString(),
            workOrder.AllowedActions.Select(action => action.ToString()).ToList(),
            equipment,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
