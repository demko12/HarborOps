using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPreventiveWorkForecastRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public Guid? VesselId { get; init; }

    public string? State { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
