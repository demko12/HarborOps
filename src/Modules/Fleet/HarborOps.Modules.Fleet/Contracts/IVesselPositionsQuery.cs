using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public interface IVesselPositionsQuery
{
    Task<VesselPositionsResponse> GetAsync(
        VesselPositionsRequest request,
        CancellationToken cancellationToken);
}
