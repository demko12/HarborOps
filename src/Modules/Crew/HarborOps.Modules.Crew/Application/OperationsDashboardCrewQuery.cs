using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class OperationsDashboardCrewQuery(DbContext dbContext)
    : IOperationsDashboardCrewQuery
{
    public async Task<OperationsDashboardCrewResult> GetExpiringCredentialsAsync(
        DateOnly asOf,
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var members = dbContext.Set<CrewMember>()
            .AsNoTracking()
            .Where(member => member.PassportExpiry <= through);
        var certificates =
            from certificate in dbContext.Set<CrewCertificate>().AsNoTracking()
            join member in dbContext.Set<CrewMember>().AsNoTracking()
                on certificate.CrewMemberId equals member.Id
            where certificate.ExpiryDate <= through
            select new { certificate, member.Name };

        var count = await members.CountAsync(cancellationToken)
            + await certificates.CountAsync(cancellationToken);
        var passportItems = await members
            .OrderBy(member => member.PassportExpiry)
            .ThenBy(member => member.Name)
            .ThenBy(member => member.Id)
            .Take(maximumItems)
            .Select(member => new OperationsDashboardCrewItem(
                member.Id,
                member.Name,
                "Passport",
                member.PassportExpiry,
                true))
            .ToListAsync(cancellationToken);
        var certificateItems = await certificates
            .OrderBy(row => row.certificate.ExpiryDate)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.certificate.Id)
            .Take(maximumItems)
            .Select(row => new OperationsDashboardCrewItem(
                row.certificate.CrewMemberId,
                row.Name,
                row.certificate.Name,
                row.certificate.ExpiryDate,
                false))
            .ToListAsync(cancellationToken);

        var items = passportItems
            .Concat(certificateItems)
            .OrderBy(item => item.ExpiryDate)
            .ThenBy(item => item.CrewMemberName, StringComparer.Ordinal)
            .ThenBy(item => item.CredentialName, StringComparer.Ordinal)
            .ThenBy(item => item.CrewMemberId)
            .Take(maximumItems)
            .ToArray();

        return new OperationsDashboardCrewResult(count, items);
    }
}
