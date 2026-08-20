using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class RecordVesselStatusChangeService(DbContext dbContext)
{
    public async Task<RecordVesselStatusChangeResponse> RecordAsync(
        Guid vesselId,
        string changedBy,
        RecordVesselStatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedBy);

        var errors = RecordVesselStatusChangeValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RecordVesselStatusChangeValidationException(errors);
        }

        var status = request.Status!.Trim();
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        var vessel = await dbContext.Set<Vessel>()
            .FirstOrDefaultAsync(v => v.Id == vesselId, cancellationToken);

        if (vessel is null)
        {
            throw new VesselNotFoundException(vesselId);
        }

        if (string.Equals(vessel.CurrentStatus, status, StringComparison.Ordinal))
        {
            throw new VesselStatusUnchangedException(vesselId, status);
        }

        vessel.ChangeStatus(status);

        var history = VesselStatusHistory.Record(vesselId, status, changedBy, notes);
        await dbContext.Set<VesselStatusHistory>().AddAsync(history, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordVesselStatusChangeResponse(
            history.Id,
            vesselId,
            status,
            changedBy,
            notes,
            history.ChangedAtUtc);
    }
}
