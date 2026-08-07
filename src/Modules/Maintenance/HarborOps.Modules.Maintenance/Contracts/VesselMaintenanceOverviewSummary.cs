using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record VesselMaintenanceOverviewSummary
(
    Guid VesselId,
    int OpenWorkOrderCount,
    int OverdueWorkOrderCount,
    int CriticalOpenWorkOrderCount,
    DateTime? NextWorkOrderDueUtc
);
