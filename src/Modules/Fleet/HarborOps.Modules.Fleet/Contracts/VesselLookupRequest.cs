using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselLookupRequest
{
    public IReadOnlyCollection<Guid>? VesselIds { get; init; }
}
