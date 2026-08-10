using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CompleteMaintenanceWorkOrderRequest
{
    public Guid? TechnicianId { get; init; }

    public DateTime? CompletedUtc { get; init; }

    public string? Notes { get; init; }

    public decimal? Cost { get; init; }

    public decimal? LaborHours { get; init; }
}
