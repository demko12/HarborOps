using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetAuditPlansQuery(DbContext dbContext, TimeProvider timeProvider)
    : IGetAuditPlansQuery
{
    public async Task<GetAuditPlansResponse> GetAsync(
        GetAuditPlansRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetAuditPlansValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetAuditPlansValidationException(errors);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var plans = dbContext.Set<AuditPlan>().AsNoTracking().AsQueryable();

        if (request.VesselId is { } vesselId)
        {
            plans = plans.Where(plan => plan.VesselId == vesselId);
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var type = request.Type.Trim();
            plans = plans.Where(plan => plan.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            plans = plans.Where(plan => plan.Status == status);
        }

        if (request.From is { } from)
        {
            plans = plans.Where(plan => plan.DueDate >= from);
        }

        if (request.To is { } to)
        {
            plans = plans.Where(plan => plan.PlannedStartDate <= to);
        }

        var totalCount = await plans.CountAsync(cancellationToken);

        var items = await plans
            .OrderBy(plan => plan.PlannedStartDate)
            .ThenBy(plan => plan.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(plan => new AuditPlanListItem(
                plan.Id,
                plan.VesselId,
                plan.Type,
                plan.Status,
                plan.PlannedStartDate,
                plan.DueDate,
                plan.AssignedInspectorUserId,
                (plan.Status == AuditPlan.ScheduledStatus || plan.Status == AuditPlan.InProgressStatus)
                    && plan.DueDate < today))
            .ToListAsync(cancellationToken);

        return new GetAuditPlansResponse(items, totalCount, request.PageNumber, request.PageSize);
    }
}
