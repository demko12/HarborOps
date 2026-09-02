using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record WorkOrderCompletionRecordItem(
    Guid MaintenanceRecordId,
    Guid TechnicianId,
    DateOnly CompletionDate,
    string? Notes,
    decimal? Cost,
    decimal? LaborHours);
