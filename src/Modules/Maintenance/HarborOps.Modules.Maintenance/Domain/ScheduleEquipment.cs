using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class ScheduleEquipment
{
    private ScheduleEquipment()
    {
    }

    private ScheduleEquipment(
        Guid scheduleId,
        Guid equipmentId)
    {
        ScheduleId = scheduleId;
        EquipmentId = equipmentId;
    }

    public Guid ScheduleId { get; private set; }

    public Guid EquipmentId { get; private set; }

    internal static ScheduleEquipment Create(
        Guid scheduleId,
        Guid equipmentId)
    {
        return new ScheduleEquipment(
            scheduleId,
            equipmentId);
    }
}
