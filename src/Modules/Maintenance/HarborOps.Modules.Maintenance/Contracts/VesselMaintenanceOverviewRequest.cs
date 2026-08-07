using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record VesselMaintenanceOverviewRequest
{
    public IReadOnlyCollection<Guid>? VesselIds { get; init; }
}
