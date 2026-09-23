using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class EndVesselManningPlanService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<EndVesselManningPlanResponse> EndAsync(
        Guid vesselManningPlanId,
        Guid actorUserId,
        EndVesselManningPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = EndVesselManningPlanValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new EndVesselManningPlanValidationException(errors);
        }

        var plan = await dbContext.Set<VesselManningPlan>()
            .FirstOrDefaultAsync(p => p.Id == vesselManningPlanId, cancellationToken);

        if (plan is null)
        {
            throw new VesselManningPlanNotFoundException(vesselManningPlanId);
        }

        var previousEndDate = plan.EffectiveEndDate;
        var newEndDate = request.NewEndDate!.Value;

        plan.EndOn(newEndDate);

        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var history = VesselManningPlanHistory.RecordEndDateChanged(
            plan.Id, actorUserId, request.Reason!.Trim(), occurredAtUtc, previousEndDate, newEndDate);

        await dbContext.Set<VesselManningPlanHistory>().AddAsync(history, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EndVesselManningPlanResponse(plan.Id, plan.EffectiveEndDate, plan.ConcurrencyToken);
    }
}
