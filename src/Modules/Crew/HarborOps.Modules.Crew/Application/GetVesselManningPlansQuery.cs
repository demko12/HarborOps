using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetVesselManningPlansQuery(DbContext dbContext) : IGetVesselManningPlansQuery
{
    public async Task<GetVesselManningPlansResponse> GetAsync(
        GetVesselManningPlansRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetVesselManningPlansValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetVesselManningPlansValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetVesselManningPlansValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetVesselManningPlansValidator.DefaultPageSize;

        var plans = dbContext.Set<VesselManningPlan>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            plans = plans.Where(plan => plan.VesselId == vesselId);
        }

        var rows =
            from plan in plans
            select new
            {
                plan.Id,
                plan.VesselId,
                plan.EffectiveStartDate,
                plan.EffectiveEndDate,
                plan.CreatedByUserId,
                plan.CreatedAtUtc,
                plan.ConcurrencyToken,
                LineCount = dbContext.Set<VesselManningPlanLine>().Count(line => line.VesselManningPlanId == plan.Id),
            };

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(row => row.EffectiveStartDate)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new VesselManningPlanListItem(
                row.Id,
                row.VesselId,
                row.EffectiveStartDate,
                row.EffectiveEndDate,
                row.LineCount,
                row.CreatedByUserId,
                row.CreatedAtUtc,
                row.ConcurrencyToken))
            .ToListAsync(cancellationToken);

        return new GetVesselManningPlansResponse(items, pageNumber, pageSize, totalCount);
    }
}
