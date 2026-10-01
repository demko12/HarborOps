using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetVesselComplianceOverviewQuery(DbContext dbContext, IDateTimeProvider? dateTimeProvider = null)
    : IGetVesselComplianceOverviewQuery
{
    internal const int MaxItemsPerCollection = 100;

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

        var asOf = DateOnly.FromDateTime(dateTimeProvider?.UtcNow ?? DateTime.UtcNow);

        var certificateRows = await dbContext.Set<ComplianceCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.VesselId == request.VesselId)
            .OrderBy(certificate => certificate.ExpiresOn)
            .ThenBy(certificate => certificate.Id)
            .Take(MaxItemsPerCollection)
            .Select(certificate => new
            {
                certificate.Id,
                certificate.VesselId,
                certificate.CertificateType,
                certificate.CertificateNumber,
                certificate.IssuingAuthority,
                certificate.IssuedOn,
                certificate.ExpiresOn,
                certificate.PredecessorCertificateId,
                certificate.RevokedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var successors = await ComplianceCertificateValidity.LoadSuccessorsAsync(
            dbContext,
            certificateRows
                .Select(row => new ComplianceCertificateSuccessorKey(row.Id, row.VesselId, row.CertificateType))
                .ToList(),
            cancellationToken);

        var certificates = certificateRows
            .Select(row =>
            {
                var successor = successors.GetValueOrDefault(row.Id);

                return new ComplianceCertificateOverviewItem(
                    row.Id,
                    row.VesselId,
                    row.CertificateType,
                    row.CertificateNumber,
                    row.IssuingAuthority,
                    row.IssuedOn,
                    row.ExpiresOn,
                    ComplianceCertificateValidity.Status(row.IssuedOn, row.ExpiresOn, row.RevokedAtUtc is not null, asOf),
                    row.PredecessorCertificateId,
                    row.RevokedAtUtc,
                    successor?.Id,
                    successor?.CertificateNumber,
                    successor?.IssuedOn,
                    successor?.IsRevoked ?? false,
                    successor is not null && ComplianceCertificateValidity.CoversExpiry(
                        row.ExpiresOn, successor.IssuedOn, successor.ExpiresOn, successor.IsRevoked),
                    successor is null ? null : ComplianceCertificateValidity.GapDays(row.ExpiresOn, successor.IssuedOn));
            })
            .ToList();

        var pscInspectionRows = await dbContext.Set<PscInspection>()
            .AsNoTracking()
            .Where(inspection => inspection.VesselId == request.VesselId)
            .OrderByDescending(inspection => inspection.Date)
            .ThenBy(inspection => inspection.Id)
            .Take(MaxItemsPerCollection)
            .Select(inspection => new
            {
                inspection.Id,
                inspection.VesselId,
                inspection.PortId,
                inspection.Date,
                inspection.InspectingAuthority,
                inspection.DeficiencyCount,
                inspection.VesselDetained,
                LatestDecision = dbContext.Set<PscDetentionDecision>()
                    .Where(decision => decision.PscInspectionId == inspection.Id)
                    .OrderByDescending(decision => decision.OccurredAtUtc)
                    .ThenByDescending(decision => decision.Id)
                    .Select(decision => decision.Decision)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var pscInspections = pscInspectionRows
            .Select(row => new PscInspectionOverviewItem(
                row.Id,
                row.VesselId,
                row.PortId,
                row.Date,
                row.InspectingAuthority,
                row.DeficiencyCount,
                row.VesselDetained,
                PscDetentionState.FromLatestDecision(row.LatestDecision, row.VesselDetained)))
            .ToList();

        var surveyAudits = await dbContext.Set<SurveyAudit>()
            .AsNoTracking()
            .Where(survey => survey.VesselId == request.VesselId)
            .OrderByDescending(survey => survey.Date)
            .ThenBy(survey => survey.Id)
            .Take(10)
            .Select(survey => new SurveyAuditOverviewItem(
                survey.Id,
                survey.VesselId,
                survey.Date,
                survey.Type,
                survey.Result))
            .ToListAsync(cancellationToken);

        var nearExpiry = asOf.AddDays(30);
        var insurancePolicies = await dbContext.Set<InsurancePolicy>()
            .AsNoTracking()
            .Where(policy => policy.VesselId == request.VesselId)
            .OrderBy(policy => policy.ExpiryDate)
            .ThenBy(policy => policy.Id)
            .Take(MaxItemsPerCollection)
            .Select(policy => new InsurancePolicyOverviewItem(
                policy.Id,
                policy.VesselId,
                policy.Insurer,
                policy.PolicyNumber,
                policy.Coverage,
                policy.ExpiryDate,
                policy.ExpiryDate < asOf
                    ? "Expired"
                    : policy.ExpiryDate <= nearExpiry
                        ? "Expiring soon"
                        : "Active"))
            .ToListAsync(cancellationToken);

        var openNonConformities = await dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity =>
                nonConformity.ClosureStatus == NonConformity.ClosureStatusOpen &&
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
            surveyAudits,
            openNonConformities,
            insurancePolicies);
    }
}
