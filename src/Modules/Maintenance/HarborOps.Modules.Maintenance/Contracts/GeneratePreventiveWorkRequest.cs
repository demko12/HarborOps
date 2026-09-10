using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GeneratePreventiveWorkRequest
{
    public DateOnly? Through { get; init; }

    public Guid? VesselId { get; init; }

    public string? Priority { get; init; }
}
