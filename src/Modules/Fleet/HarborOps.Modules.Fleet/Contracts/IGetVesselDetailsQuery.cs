using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public interface IGetVesselDetailsQuery
{
    Task<VesselDetailsResponse?> GetAsync(Guid vesselId, CancellationToken cancellationToken);
}
