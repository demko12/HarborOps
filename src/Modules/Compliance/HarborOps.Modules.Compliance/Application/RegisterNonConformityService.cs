using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RegisterNonConformityService(DbContext dbContext, IDateTimeProvider dateTimeProvider)
{
    public async Task<RegisterNonConformityResponse> RegisterAsync(
        RegisterNonConformityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterNonConformityValidator.Validate(request, dateTimeProvider);
        if (errors.Count > 0)
        {
            throw new RegisterNonConformityValidationException(errors);
        }

        var evidenceDocumentId = request.EvidenceDocumentId!.Value;
        var severity = request.Severity!.Trim();
        var correctiveActionDeadline = request.CorrectiveActionDeadline!.Value;

        if (request.SurveyId is not null)
        {
            var surveyExists = await dbContext.Set<SurveyAudit>()
                .AsNoTracking()
                .AnyAsync(survey => survey.Id == request.SurveyId.Value, cancellationToken);

            if (!surveyExists)
            {
                throw new SurveyAuditNotFoundException(request.SurveyId.Value);
            }
        }
        else
        {
            var pscInspectionExists = await dbContext.Set<PscInspection>()
                .AsNoTracking()
                .AnyAsync(psc => psc.Id == request.PscId!.Value, cancellationToken);

            if (!pscInspectionExists)
            {
                throw new PscInspectionNotFoundException(request.PscId!.Value);
            }
        }

        var nonConformity = NonConformity.Register(
            evidenceDocumentId,
            severity,
            correctiveActionDeadline,
            request.SurveyId,
            request.PscId);

        var nonConformities = dbContext.Set<NonConformity>();

        await nonConformities.AddAsync(nonConformity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterNonConformityResponse(nonConformity.Id);
    }
}
