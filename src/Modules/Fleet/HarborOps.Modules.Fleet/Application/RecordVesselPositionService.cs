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

        var reportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        if (dbContext.Database.IsRelational())
        {
            var updatedRows = await dbContext.Set<Vessel>()
                .Where(vessel => vessel.Id == vesselId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(vessel => vessel.CurrentLatitude, latitude)
                        .SetProperty(vessel => vessel.CurrentLongitude, longitude)
                        .SetProperty(vessel => vessel.LastPositionReportedAtUtc, reportedAtUtc)
                        .SetProperty(vessel => vessel.UpdatedAtUtc, reportedAtUtc),
                    cancellationToken);

            if (updatedRows == 0)
            {
                throw new VesselNotFoundException(vesselId);
            }
        }
        else
        {
            var vessel = await dbContext.Set<Vessel>()
                .FirstOrDefaultAsync(vessel => vessel.Id == vesselId, cancellationToken)
                ?? throw new VesselNotFoundException(vesselId);

            vessel.ReportPosition(latitude, longitude, reportedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new RecordVesselPositionResponse(vesselId, latitude, longitude, reportedAtUtc);
    }
}
