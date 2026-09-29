using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class ExpiryAlertCandidateQuery(DbContext dbContext) : IExpiryAlertCandidateQuery
{
    public async Task<IReadOnlyList<ComplianceExpiryAlertCandidate>> GetCandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var certificates = dbContext.Set<ComplianceCertificate>().AsNoTracking();
        var candidates = certificates.Where(x => x.RevokedAtUtc == null && x.ExpiresOn >= from && x.ExpiresOn <= to);

        return await ComplianceCertificateValidity.WithoutCoveringSuccessor(candidates, certificates)
            .OrderBy(x => x.ExpiresOn)
            .ThenBy(x => x.Id)
            .Select(x => new ComplianceExpiryAlertCandidate(x.Id, x.VesselId, x.ExpiresOn, "certificate"))
            .ToListAsync(cancellationToken);
    }
}
