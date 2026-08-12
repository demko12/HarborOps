using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetMaintenanceWorkOrdersRequest
{
    public Guid? VesselId { get; init; }

    public string? Status { get; init; }

    public string? Priority { get; init; }

    public bool? Overdue { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
