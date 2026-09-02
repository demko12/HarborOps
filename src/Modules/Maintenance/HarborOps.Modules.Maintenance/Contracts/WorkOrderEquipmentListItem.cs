using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record WorkOrderEquipmentListItem(
    Guid EquipmentId,
    Guid VesselId,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);
