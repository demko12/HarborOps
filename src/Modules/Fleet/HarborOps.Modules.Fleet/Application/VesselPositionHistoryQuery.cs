using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class VesselPositionHistoryQuery(DbContext dbContext, TimeProvider timeProvider) : IVesselPositionHistoryQuery
{
    public async Task<VesselPositionHistoryResponse> GetAsync(Guid vesselId, VesselPositionHistoryRequest request, CancellationToken cancellationToken)
    {
        var toUtc = request.ToUtc ?? timeProvider.GetUtcNow().UtcDateTime; var fromUtc = request.FromUtc ?? toUtc.AddDays(-30);
        var pageNumber = request.PageNumber ?? 1; var pageSize = request.PageSize ?? 50;
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc || fromUtc > toUtc || toUtc - fromUtc > TimeSpan.FromDays(90) || pageNumber < 1 || pageSize is < 1 or > 100) throw new VesselPositionHistoryValidationException();
        var reports = dbContext.Set<VesselPositionReport>().AsNoTracking().Where(x => x.VesselId == vesselId && x.ReportedAtUtc >= fromUtc && x.ReportedAtUtc <= toUtc);
        var total = await reports.CountAsync(cancellationToken);
        var items = await reports.OrderByDescending(x => x.ReportedAtUtc).ThenByDescending(x => x.Sequence).ThenByDescending(x => x.Id).Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(x => new VesselPositionHistoryItem(x.Id, x.Latitude, x.Longitude, x.ReportedAtUtc, x.ReportedByUserId, null)).ToListAsync(cancellationToken);
        return new(items, pageNumber, pageSize, total);
    }
}
internal sealed class VesselPositionHistoryValidationException : Exception { }
