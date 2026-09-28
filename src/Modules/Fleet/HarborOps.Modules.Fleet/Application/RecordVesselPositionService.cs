using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class RecordVesselPositionService(DbContext dbContext, TimeProvider timeProvider)
{
    public Task<RecordVesselPositionResponse> RecordAsync(
        Guid vesselId,
        RecordVesselPositionRequest request,
        CancellationToken cancellationToken) =>
        RecordAsync(vesselId, request, Guid.Empty, cancellationToken);

    public async Task<RecordVesselPositionResponse> RecordAsync(
        Guid vesselId,
        RecordVesselPositionRequest request,
        Guid reportedByUserId,
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

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var vessel = await dbContext.Set<Vessel>().FirstOrDefaultAsync(vessel => vessel.Id == vesselId, cancellationToken)
            ?? throw new VesselNotFoundException(vesselId);
        var sequence = (await dbContext.Set<VesselPositionReport>().Where(report => report.VesselId == vesselId).MaxAsync(report => (long?)report.Sequence, cancellationToken) ?? 0) + 1;
        await dbContext.Set<VesselPositionReport>().AddAsync(VesselPositionReport.Create(vesselId, latitude, longitude, reportedByUserId, reportedAtUtc, sequence), cancellationToken);
        vessel.ReportPosition(latitude, longitude, reportedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new RecordVesselPositionResponse(vesselId, latitude, longitude, reportedAtUtc);
    }
}
