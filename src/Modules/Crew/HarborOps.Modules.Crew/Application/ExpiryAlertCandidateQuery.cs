using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class ExpiryAlertCandidateQuery(DbContext dbContext) : IExpiryAlertCandidateQuery
{
    public async Task<IReadOnlyList<CrewExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await dbContext.Set<CrewCertificate>().AsNoTracking().Where(x => x.Status == "Active" && x.ExpiryDate >= from && x.ExpiryDate <= to).OrderBy(x => x.ExpiryDate).ThenBy(x => x.Id).Select(x => new CrewExpiryAlertCandidate(x.Id, x.CrewMemberId, x.ExpiryDate, "certificate")).ToListAsync(cancellationToken);
}
