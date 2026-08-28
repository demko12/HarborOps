using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselPositionsRequest
{
    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    public Guid? FleetId { get; init; }

    public string? Status { get; init; }

    public bool? StaleOnly { get; init; }
}
