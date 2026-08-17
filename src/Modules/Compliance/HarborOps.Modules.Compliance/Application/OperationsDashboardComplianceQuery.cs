using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class OperationsDashboardComplianceQuery(DbContext dbContext)
    : IOperationsDashboardComplianceQuery
{
    public async Task<OperationsDashboardComplianceResult> GetAttentionItemsAsync(
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var certificates = dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.ExpiresOn <= through);
        var nonConformities = dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity => nonConformity.ClosureStatus == "Open" && nonConformity.Severity == "Critical");

        var count = await certificates.CountAsync(cancellationToken)
            + await nonConformities.CountAsync(cancellationToken);
        var certificateItems = await certificates
            .OrderBy(certificate => certificate.ExpiresOn)
            .ThenBy(certificate => certificate.Id)
            .Take(maximumItems)
            .Select(certificate => new OperationsDashboardComplianceItem(
                "Certificate",
                certificate.CertificateType,
                certificate.ExpiresOn,
                "Expiring",
                certificate.Id.ToString()))
            .ToListAsync(cancellationToken);
        var nonConformityItems = await nonConformities
            .OrderBy(nonConformity => nonConformity.CorrectiveActionDeadline)
            .ThenBy(nonConformity => nonConformity.Id)
            .Take(maximumItems)
            .Select(nonConformity => new OperationsDashboardComplianceItem(
                "Non-conformity",
                "Critical non-conformity",
                nonConformity.CorrectiveActionDeadline,
                nonConformity.Severity,
                nonConformity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);

        var items = certificateItems
            .Concat(nonConformityItems)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.ItemType, StringComparer.Ordinal)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .ThenBy(item => item.SortKey, StringComparer.Ordinal)
            .Take(maximumItems)
            .ToArray();

        return new OperationsDashboardComplianceResult(count, items);
    }
}
