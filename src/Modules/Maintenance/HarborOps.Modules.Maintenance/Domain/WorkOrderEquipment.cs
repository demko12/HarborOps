using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class WorkOrderEquipment
{
    private WorkOrderEquipment()
    {
    }

    private WorkOrderEquipment(Guid workOrderId, Guid equipmentId)
    {
        WorkOrderId = workOrderId;
        EquipmentId = equipmentId;
    }

    public Guid WorkOrderId { get; private set; }

    public Guid EquipmentId { get; private set; }

    internal static WorkOrderEquipment Create(
        Guid workOrderId,
        Guid equipmentId)
    {
        return new WorkOrderEquipment(
            workOrderId,
            equipmentId);
    }
}
