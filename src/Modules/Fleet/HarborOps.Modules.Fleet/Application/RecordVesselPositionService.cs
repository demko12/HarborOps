using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class RecordVesselPositionService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RecordVesselPositionResponse> RecordAsync(
        Guid vesselId,
        RecordVesselPositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RecordVesselPositionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RecordVesselPositionValidationException(errors);
        }

        var vessel = await dbContext.Set<Vessel>()
            .FirstOrDefaultAsync(v => v.Id == vesselId, cancellationToken)
            ?? throw new VesselNotFoundException(vesselId);

        var reportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        vessel.ReportPosition(latitude, longitude, reportedAtUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordVesselPositionResponse(vesselId, latitude, longitude, reportedAtUtc);
    }
}
