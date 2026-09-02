using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class ExpiryAlertCandidateQuery(DbContext dbContext) : IExpiryAlertCandidateQuery
{
    public async Task<IReadOnlyList<ComplianceExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await dbContext.Set<ComplianceCertificate>().AsNoTracking().Where(x => x.ExpiresOn >= from && x.ExpiresOn <= to).OrderBy(x => x.ExpiresOn).ThenBy(x => x.Id).Select(x => new ComplianceExpiryAlertCandidate(x.Id, x.VesselId, x.ExpiresOn, "certificate")).ToListAsync(cancellationToken);
}
