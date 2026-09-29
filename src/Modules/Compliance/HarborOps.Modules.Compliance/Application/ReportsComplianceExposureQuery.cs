using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class ReportsComplianceExposureQuery(DbContext dbContext) : IReportsComplianceExposureQuery
{
    internal const string CategoryCertificate = "Certificate";
    internal const string CategoryInsurance = "Insurance";
    internal const string CategoryDetention = "Detention";
    internal const string CategoryNonConformity = "NonConformity";
    internal const string StatusRevoked = ComplianceCertificate.RevokedStatus;
    internal const string StatusRenewed = "Renewed";
    internal const string StatusNotYetEffective = "Not yet effective";

    private const int ExposureWindowDays = 60;
    private const int MaxCandidatesPerCategory = 2000;

    public async Task<ReportsComplianceExposureResult> SearchAsync(
        ReportsComplianceExposureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var exposureThrough = request.AsOf.AddDays(ExposureWindowDays);

        var candidates = new List<ReportsComplianceExposureRow>();

        if (IncludesCategory(request.Category, CategoryCertificate))
        {
            candidates.AddRange(await GetCertificateRowsAsync(request.VesselId, exposureThrough, request.AsOf, cancellationToken));
        }

        if (IncludesCategory(request.Category, CategoryInsurance))
        {
            candidates.AddRange(await GetInsuranceRowsAsync(request.VesselId, exposureThrough, request.AsOf, cancellationToken));
        }

        if (IncludesCategory(request.Category, CategoryDetention))
        {
            candidates.AddRange(await GetDetentionRowsAsync(request.VesselId, cancellationToken));
        }

        if (IncludesCategory(request.Category, CategoryNonConformity))
        {
            candidates.AddRange(await GetOpenNonConformityRowsAsync(request.VesselId, cancellationToken));
        }

        var ordered = candidates
            .OrderBy(row => row.RelevantDate)
            .ThenBy(row => row.ItemType, StringComparer.Ordinal)
            .ThenBy(row => row.Reference, StringComparer.Ordinal)
            .ToList();

        var totalCount = ordered.Count;
        var items = ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ReportsComplianceExposureResult(items, pageNumber, pageSize, totalCount);
    }

    private async Task<List<ReportsComplianceExposureRow>> GetCertificateRowsAsync(
        Guid? vesselId, DateOnly exposureThrough, DateOnly asOf, CancellationToken cancellationToken)
    {
        var certificates = dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.ExpiresOn <= exposureThrough || certificate.RevokedAtUtc != null);

        if (vesselId is { } id)
        {
            certificates = certificates.Where(certificate => certificate.VesselId == id);
        }

        var rows = await certificates
            .OrderBy(certificate => certificate.ExpiresOn)
            .ThenBy(certificate => certificate.Id)
            .Take(MaxCandidatesPerCategory)
            .Select(certificate => new
            {
                certificate.Id,
                certificate.VesselId,
                certificate.CertificateNumber,
                certificate.CertificateType,
                certificate.IssuedOn,
                certificate.ExpiresOn,
                certificate.RevokedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var successors = await ComplianceCertificateValidity.LoadSuccessorsAsync(
            dbContext,
            rows
                .Where(row => row.RevokedAtUtc is null)
                .Select(row => new ComplianceCertificateSuccessorKey(row.Id, row.VesselId, row.CertificateType))
                .ToList(),
            cancellationToken);

        return rows
            .Select(row =>
            {
                if (row.RevokedAtUtc is { } revokedAtUtc)
                {
                    return new ReportsComplianceExposureRow(
                        CategoryCertificate,
                        row.VesselId,
                        row.CertificateNumber,
                        row.CertificateType,
                        DateOnly.FromDateTime(revokedAtUtc),
                        StatusRevoked,
                        null);
                }

                var successor = successors.GetValueOrDefault(row.Id);
                var status = successor is not null && ComplianceCertificateValidity.CoversExpiry(
                        row.ExpiresOn, successor.IssuedOn, successor.ExpiresOn, successor.IsRevoked)
                    ? StatusRenewed
                    : row.IssuedOn > asOf
                        ? StatusNotYetEffective
                        : row.ExpiresOn < asOf ? "Expired" : "Expiring";

                return new ReportsComplianceExposureRow(
                    CategoryCertificate,
                    row.VesselId,
                    row.CertificateNumber,
                    row.CertificateType,
                    row.ExpiresOn,
                    status,
                    null);
            })
            .ToList();
    }

    private async Task<List<ReportsComplianceExposureRow>> GetInsuranceRowsAsync(
        Guid? vesselId, DateOnly exposureThrough, DateOnly asOf, CancellationToken cancellationToken)
    {
        var policies = dbContext.Set<InsurancePolicy>()
            .AsNoTracking()
            .Where(policy => policy.ExpiryDate <= exposureThrough);

        if (vesselId is { } id)
        {
            policies = policies.Where(policy => policy.VesselId == id);
        }

        var rows = await policies
            .OrderBy(policy => policy.ExpiryDate)
            .ThenBy(policy => policy.Id)
            .Take(MaxCandidatesPerCategory)
            .Select(policy => new
            {
                policy.VesselId,
                policy.PolicyNumber,
                policy.Insurer,
                policy.Coverage,
                policy.ExpiryDate,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ReportsComplianceExposureRow(
                CategoryInsurance,
                row.VesselId,
                row.PolicyNumber,
                $"{row.Insurer} · {row.Coverage}",
                row.ExpiryDate,
                row.ExpiryDate < asOf ? "Expired" : "Expiring",
                null))
            .ToList();
    }

    private async Task<List<ReportsComplianceExposureRow>> GetDetentionRowsAsync(
        Guid? vesselId, CancellationToken cancellationToken)
    {
        var inspections = dbContext.Set<PscInspection>()
            .AsNoTracking()
            .Where(inspection => inspection.VesselDetained);

        if (vesselId is { } id)
        {
            inspections = inspections.Where(inspection => inspection.VesselId == id);
        }

        var rows = await inspections
            .OrderByDescending(inspection => inspection.Date)
            .ThenBy(inspection => inspection.Id)
            .Take(MaxCandidatesPerCategory)
            .Select(inspection => new
            {
                inspection.Id,
                inspection.VesselId,
                inspection.InspectingAuthority,
                inspection.DeficiencyCount,
                inspection.Date,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ReportsComplianceExposureRow(
                CategoryDetention,
                row.VesselId,
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{row.InspectingAuthority} · {row.DeficiencyCount} deficiencies",
                row.Date,
                "Detained",
                null))
            .ToList();
    }

    private async Task<List<ReportsComplianceExposureRow>> GetOpenNonConformityRowsAsync(
        Guid? vesselId, CancellationToken cancellationToken)
    {
        var nonConformities = dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity =>
                nonConformity.ClosureStatus == NonConformity.ClosureStatusOpen
                && (nonConformity.Psc != null || nonConformity.Survey != null));

        if (vesselId is { } id)
        {
            nonConformities = nonConformities.Where(nonConformity =>
                (nonConformity.Psc != null && nonConformity.Psc.VesselId == id)
                || (nonConformity.Survey != null && nonConformity.Survey.VesselId == id));
        }

        var rows = await nonConformities
            .OrderBy(nonConformity => nonConformity.CorrectiveActionDeadline)
            .ThenBy(nonConformity => nonConformity.Id)
            .Take(MaxCandidatesPerCategory)
            .Select(nonConformity => new
            {
                nonConformity.Id,
                VesselId = nonConformity.Psc != null ? nonConformity.Psc.VesselId : nonConformity.Survey!.VesselId,
                nonConformity.Severity,
                nonConformity.CorrectiveActionDeadline,
                nonConformity.ClosureStatus,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ReportsComplianceExposureRow(
                CategoryNonConformity,
                row.VesselId,
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{row.Severity} non-conformity",
                row.CorrectiveActionDeadline,
                row.ClosureStatus,
                row.Severity))
            .ToList();
    }

    private static bool IncludesCategory(string? requestedCategory, string category) =>
        string.IsNullOrWhiteSpace(requestedCategory) || string.Equals(requestedCategory, category, StringComparison.OrdinalIgnoreCase);
}
