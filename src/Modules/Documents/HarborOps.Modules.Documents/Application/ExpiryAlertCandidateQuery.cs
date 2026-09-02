using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class ExpiryAlertCandidateQuery(DbContext dbContext) : IExpiryAlertCandidateQuery
{
    public async Task<IReadOnlyList<ExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await dbContext.Set<Document>().AsNoTracking()
            .Where(x => x.Status == Document.ActiveStatus && x.ExpiryDate >= from && x.ExpiryDate <= to)
            .OrderBy(x => x.ExpiryDate).ThenBy(x => x.Id)
            .Select(x => new ExpiryAlertCandidate(x.Id, x.VesselId, x.ExpiryDate, "document"))
            .ToListAsync(cancellationToken);
}
