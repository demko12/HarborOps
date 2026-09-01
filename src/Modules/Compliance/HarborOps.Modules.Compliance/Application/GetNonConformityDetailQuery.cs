using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetNonConformityDetailQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetNonConformityDetailQuery
{
    public async Task<GetNonConformityDetailResponse?> GetAsync(
        int nonConformityId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var detail = await dbContext.Set<NonConformity>()
            .AsNoTracking()
            .Where(nonConformity => nonConformity.Id == nonConformityId)
            .Select(nonConformity => new
            {
                nonConformity.Id,
                VesselId = nonConformity.Psc != null
                    ? nonConformity.Psc.VesselId
                    : nonConformity.Survey!.VesselId,
                nonConformity.SurveyId,
                nonConformity.PscId,
                nonConformity.Severity,
                nonConformity.ClosureStatus,
                nonConformity.CorrectiveActionDeadline,
                nonConformity.EvidenceDocumentId,
                nonConformity.ClosureNotes,
                nonConformity.ClosedAtUtc,
                nonConformity.ClosedByUserId,
                nonConformity.FinalEvidenceDocumentId,
                CorrectiveActions = nonConformity.CorrectiveActions
                    .OrderByDescending(correctiveAction => correctiveAction.RecordedAtUtc)
                    .Select(correctiveAction => new CorrectiveActionItem(
                        correctiveAction.Id,
                        correctiveAction.ActionText,
                        correctiveAction.ActorUserId,
                        correctiveAction.RecordedAtUtc,
                        correctiveAction.EvidenceDocumentId))
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is null)
        {
            return null;
        }

        var isOverdue = detail.ClosureStatus != NonConformity.ClosureStatusClosed
            && detail.CorrectiveActionDeadline < today;

        return new GetNonConformityDetailResponse(
            detail.Id,
            detail.VesselId,
            detail.SurveyId,
            detail.PscId,
            detail.Severity,
            detail.ClosureStatus,
            detail.CorrectiveActionDeadline,
            isOverdue,
            detail.EvidenceDocumentId,
            detail.ClosureNotes,
            detail.ClosedAtUtc,
            detail.ClosedByUserId,
            detail.FinalEvidenceDocumentId,
            detail.CorrectiveActions);
    }
}
