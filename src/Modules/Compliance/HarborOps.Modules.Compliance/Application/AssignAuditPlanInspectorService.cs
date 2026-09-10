using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class AssignAuditPlanInspectorService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<AssignAuditPlanInspectorResponse> AssignAsync(
        Guid auditPlanId,
        Guid actingUserId,
        AssignAuditPlanInspectorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = AssignAuditPlanInspectorValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new AssignAuditPlanInspectorValidationException(errors);
        }

        var plan = await dbContext.Set<AuditPlan>()
            .SingleOrDefaultAsync(candidate => candidate.Id == auditPlanId, cancellationToken)
            ?? throw new AuditPlanNotFoundException(auditPlanId);

        if (plan.Status is not (AuditPlan.ScheduledStatus or AuditPlan.InProgressStatus))
        {
            throw new InvalidAuditPlanTransitionException(plan.Id, plan.Status, AuditPlan.AssignAction);
        }

        var inspectorUserId = request.InspectorUserId!.Value;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var previousInspectorUserId = plan.AssignedInspectorUserId;
        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        plan.Assign(inspectorUserId);

        var history = AuditPlanHistory.RecordAssigned(
            plan.Id,
            reason,
            actingUserId,
            occurredAtUtc,
            plan.Status,
            previousInspectorUserId,
            inspectorUserId);

        await dbContext.Set<AuditPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignAuditPlanInspectorResponse(plan.Id, inspectorUserId);
    }
}
