using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class RegisterAuditPlanService(DbContext dbContext)
{
    public async Task<RegisterAuditPlanResponse> RegisterAsync(
        RegisterAuditPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterAuditPlanValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterAuditPlanValidationException(errors);
        }

        var vesselId = request.VesselId!.Value;
        var type = request.Type!.Trim();
        var plannedStartDate = request.PlannedStartDate!.Value;
        var dueDate = request.DueDate!.Value;
        var scope = request.Scope!.Trim();
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        await AuditPlanOverlap.EnsureNoOverlapAsync(
            dbContext, vesselId, type, plannedStartDate, dueDate, excludePlanId: null, cancellationToken);

        var plan = AuditPlan.Create(
            vesselId,
            type,
            plannedStartDate,
            dueDate,
            request.AssignedInspectorUserId,
            scope,
            notes);

        await dbContext.Set<AuditPlan>().AddAsync(plan, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterAuditPlanResponse(plan.Id);
    }
}
