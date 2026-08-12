using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public interface IVesselLookupService
{
    Task<VesselLookupResponse> LookupAsync(
        VesselLookupRequest request,
        CancellationToken cancellationToken);
}
