using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class OperationsDashboardComplianceQuery(DbContext dbContext, TimeProvider timeProvider)
    : IOperationsDashboardComplianceQuery
{
    internal const string RevokedCertificateItemType = "Revoked certificate";

    public async Task<OperationsDashboardComplianceResult> GetAttentionItemsAsync(
        DateOnly through,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var allCertificates = dbContext.Set<ComplianceCertificate>().AsNoTracking();
        var certificates = ComplianceCertificateValidity.WithoutCoveringSuccessor(
            allCertificates.Where(certificate => certificate.RevokedAtUtc == null && certificate.ExpiresOn <= through),
            allCertificates);
        var revokedCertificates = allCertificates.Where(certificate =>
            certificate.RevokedAtUtc != null
            && certificate.ExpiresOn >= today
            && !allCertificates.Any(replacement =>
                replacement.VesselId == certificate.VesselId
                && replacement.CertificateType == certificate.CertificateType
                && replacement.RevokedAtUtc == null
                && replacement.IssuedOn <= today
                && replacement.ExpiresOn >= today));
        var nonConformities = dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity => nonConformity.ClosureStatus == "Open" && nonConformity.Severity == "Critical");

        var count = await certificates.CountAsync(cancellationToken)
            + await revokedCertificates.CountAsync(cancellationToken)
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
        var revokedRows = await revokedCertificates
            .OrderBy(certificate => certificate.RevokedAtUtc)
            .ThenBy(certificate => certificate.Id)
            .Take(maximumItems)
            .Select(certificate => new { certificate.Id, certificate.CertificateType, certificate.RevokedAtUtc })
            .ToListAsync(cancellationToken);
        var revokedItems = revokedRows
            .Select(certificate => new OperationsDashboardComplianceItem(
                RevokedCertificateItemType,
                certificate.CertificateType,
                DateOnly.FromDateTime(certificate.RevokedAtUtc!.Value),
                ComplianceCertificate.RevokedStatus,
                certificate.Id.ToString()));
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
            .Concat(revokedItems)
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
