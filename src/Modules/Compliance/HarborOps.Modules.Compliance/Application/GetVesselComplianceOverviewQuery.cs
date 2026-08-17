using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetVesselComplianceOverviewQuery(DbContext dbContext)
    : IGetVesselComplianceOverviewQuery
{
    internal const int MaxItemsPerCollection = 100;
    private const string OpenClosureStatus = "Open";

    public async Task<GetVesselComplianceOverviewResponse> GetAsync(
        GetVesselComplianceOverviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetVesselComplianceOverviewValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetVesselComplianceOverviewValidationException(errors);
        }

        var certificates = await dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.VesselId == request.VesselId)
            .OrderBy(certificate => certificate.ExpiresOn)
            .ThenBy(certificate => certificate.Id)
            .Take(MaxItemsPerCollection)
            .Select(certificate => new ComplianceCertificateOverviewItem(
                certificate.Id,
                certificate.VesselId,
                certificate.CertificateType,
                certificate.CertificateNumber,
                certificate.IssuingAuthority,
                certificate.IssuedOn,
                certificate.ExpiresOn))
            .ToListAsync(cancellationToken);

        var pscInspections = await dbContext.Set<PscInspection>()
            .AsNoTracking()
            .Where(inspection => inspection.VesselId == request.VesselId)
            .OrderByDescending(inspection => inspection.Date)
            .ThenBy(inspection => inspection.Id)
            .Take(MaxItemsPerCollection)
            .Select(inspection => new PscInspectionOverviewItem(
                inspection.Id,
                inspection.VesselId,
                inspection.PortId,
                inspection.Date,
                inspection.InspectingAuthority,
                inspection.DeficiencyCount,
                inspection.VesselDetained))
            .ToListAsync(cancellationToken);

        var openNonConformities = await dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity =>
                nonConformity.ClosureStatus == OpenClosureStatus &&
                ((nonConformity.Psc != null && nonConformity.Psc.VesselId == request.VesselId) ||
                 (nonConformity.Survey != null && nonConformity.Survey.VesselId == request.VesselId)))
            .OrderBy(nonConformity => nonConformity.CorrectiveActionDeadline)
            .ThenBy(nonConformity => nonConformity.Id)
            .Take(MaxItemsPerCollection)
            .Select(nonConformity => new OpenNonConformityOverviewItem(
                nonConformity.Id,
                nonConformity.Psc != null
                    ? nonConformity.Psc.VesselId
                    : nonConformity.Survey!.VesselId,
                nonConformity.EvidenceDocumentId,
                nonConformity.Severity,
                nonConformity.CorrectiveActionDeadline,
                nonConformity.ClosureStatus))
            .ToListAsync(cancellationToken);

        return new GetVesselComplianceOverviewResponse(
            certificates,
            pscInspections,
            openNonConformities);
    }
}
