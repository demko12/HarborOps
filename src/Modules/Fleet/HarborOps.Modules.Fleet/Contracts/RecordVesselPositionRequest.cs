using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RecordVesselPositionRequest
{
    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }
}
