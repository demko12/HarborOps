using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetVesselMaintenanceOverviewService(DbContext dbContext, TimeProvider timeProvider)
    : IVesselMaintenanceOverviewQuery
{
    private static readonly WorkOrderStatus[] OpenStatuses =
        [WorkOrderStatus.Open, WorkOrderStatus.InProgress];

    public async Task<VesselMaintenanceOverviewResponse> GetVesselMaintenanceOverviewAsync(
        VesselMaintenanceOverviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetVesselMaintenanceOverviewValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetVesselMaintenanceOverviewValidationException(errors);
        }

        var vesselIds = request.VesselIds!.Distinct().ToArray();
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var summaries = await dbContext
            .Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .Where(workOrder =>
                vesselIds.Contains(workOrder.VesselId) &&
                OpenStatuses.Contains(workOrder.Status))
            .GroupBy(workOrder => workOrder.VesselId)
            .Select(group => new VesselMaintenanceOverviewSummary(
                group.Key,
                group.Count(),
                group.Count(workOrder => workOrder.DueUtc < utcNow),
                group.Count(workOrder => workOrder.Priority == WorkOrderPriority.Critical),
                group.Where(workOrder => workOrder.DueUtc >= utcNow)
                     .Min(workOrder => (DateTime?)workOrder.DueUtc)))
            .ToListAsync(cancellationToken);

        var summariesByVesselId = summaries.ToDictionary(summary => summary.VesselId);

        var result = vesselIds
            .Select(vesselId => summariesByVesselId.TryGetValue(vesselId, out var summary)
                ? summary
                : new VesselMaintenanceOverviewSummary(vesselId, 0, 0, 0, null))
            .ToList();

        return new VesselMaintenanceOverviewResponse(result);
    }
}
