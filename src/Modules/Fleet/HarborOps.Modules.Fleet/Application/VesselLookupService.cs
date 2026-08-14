using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class VesselLookupService(DbContext dbContext) : IVesselLookupService
{
    public async Task<VesselLookupResponse> LookupAsync(
        VesselLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = VesselLookupValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new VesselLookupValidationException(errors);
        }

        var vesselIds = request.VesselIds!.Distinct().ToArray();

        var vessels = await dbContext.Set<Vessel>()
            .AsNoTracking()
            .Where(vessel => vesselIds.Contains(vessel.Id))
            .Select(vesselIds => new VesselLookupItem(vesselIds.Id, vesselIds.Name))
            .ToListAsync(cancellationToken);

        return new VesselLookupResponse(vessels);
    }
}
