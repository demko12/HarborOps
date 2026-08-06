using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CreateMaintenanceWorkOrderRequest
{
    public Guid? VesselId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Priority { get; init; }

    public DateTime? DueUtc { get; init; }

}
