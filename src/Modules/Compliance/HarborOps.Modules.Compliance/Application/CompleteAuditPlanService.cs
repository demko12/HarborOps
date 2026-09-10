using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class CompleteAuditPlanService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CompleteAuditPlanResponse> CompleteAsync(
        Guid auditPlanId,
        Guid actingUserId,
        CompleteAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(occurredAtUtc);

        var errors = CompleteAuditPlanValidator.Validate(request, today);
        if (errors.Count > 0)
        {
            throw new CompleteAuditPlanValidationException(errors);
        }

        var plan = await dbContext.Set<AuditPlan>()
            .SingleOrDefaultAsync(candidate => candidate.Id == auditPlanId, cancellationToken)
            ?? throw new AuditPlanNotFoundException(auditPlanId);

        // The guard is checked before anything is added to the change tracker, so a rejected
        // completion (wrong status, including an already-completed plan) leaves zero trace -
        // no SurveyAudit row and no history row are ever created.
        if (plan.Status != AuditPlan.InProgressStatus)
        {
            throw new InvalidAuditPlanTransitionException(plan.Id, plan.Status, AuditPlan.CompleteAction);
        }

        var previousStatus = plan.Status;
        var result = request.Result!.Trim();
        var findings = request.Findings!.Trim();

        var survey = new SurveyAudit(
            plan.VesselId,
            request.ActualAuditDate!.Value,
            plan.Type,
            result,
            findings,
            request.DocumentId,
            inspectorName: null);

        await dbContext.Set<SurveyAudit>().AddAsync(survey, cancellationToken);

        // Setting the navigation (rather than a scalar SurveyAuditId, which isn't known until
        // the identity column is generated) lets EF Core's relationship fix-up populate the FK
        // from survey.Id as part of this same SaveChangesAsync call - one atomic transaction
        // creates the survey, links the plan to it, and records the history row together.
        plan.Complete(survey, occurredAtUtc);

        var history = AuditPlanHistory.RecordCompleted(plan.Id, actingUserId, occurredAtUtc, previousStatus, plan.Status);
        await dbContext.Set<AuditPlanHistory>().AddAsync(history, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompleteAuditPlanResponse(plan.Id, survey.Id, occurredAtUtc);
    }
}
