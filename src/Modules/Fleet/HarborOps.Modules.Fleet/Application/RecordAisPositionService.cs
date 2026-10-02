using System.Data;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class RecordAisPositionService(DbContext database)
{
    internal async Task RecordAsync(AisObservation observation, CancellationToken cancellationToken)
    {
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        // Recheck the registry: the MMSI may have been edited since subscription.
        var vessel = await database.Set<Vessel>().SingleOrDefaultAsync(
            vessel => vessel.ImoNumber == observation.Imo && vessel.Mmsi == observation.Mmsi, cancellationToken);
        var position = observation.Position;
        if (vessel is null || vessel.LastPositionReportedAtUtc >= position.ReportedAtUtc) return;

        var sequence = (await database.Set<VesselPositionReport>()
            .Where(report => report.VesselId == vessel.Id)
            .MaxAsync(report => (long?)report.Sequence, cancellationToken) ?? 0) + 1;
        database.Set<VesselPositionReport>().Add(VesselPositionReport.Create(
            vessel.Id, position.Latitude, position.Longitude, Guid.Empty, position.ReportedAtUtc, sequence));
        vessel.ReportPosition(position.Latitude, position.Longitude, position.ReportedAtUtc);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
