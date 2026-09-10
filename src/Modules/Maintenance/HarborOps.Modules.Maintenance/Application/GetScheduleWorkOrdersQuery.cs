using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetScheduleWorkOrdersQuery(DbContext dbContext)
{
    public async Task<GetScheduleWorkOrdersResponse> GetWorkOrdersAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var scheduleExists = await dbContext.Set<MaintenanceSchedule>()
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == scheduleId, cancellationToken);

        if (!scheduleExists)
        {
            throw new MaintenanceScheduleNotFoundException(scheduleId);
        }

        var rows = await (
            from occurrence in dbContext.Set<ScheduleOccurrence>().AsNoTracking()
            join workOrder in dbContext.Set<MaintenanceWorkOrder>().AsNoTracking()
                on occurrence.WorkOrderId equals workOrder.Id
            where occurrence.ScheduleId == scheduleId
            orderby occurrence.DueDate, occurrence.ScheduleId, occurrence.Id
            select new
            {
                WorkOrderId = workOrder.Id,
                OccurrenceId = occurrence.Id,
                workOrder.VesselId,
                occurrence.DueDate,
                workOrder.Title,
                workOrder.Status,
                workOrder.Priority,
                OccurrenceState = occurrence.State,
                workOrder.DueUtc,
                workOrder.CreatedUtc,
                workOrder.CompletedUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ScheduleWorkOrderListItem(
                row.WorkOrderId,
                row.OccurrenceId,
                row.VesselId,
                row.DueDate,
                row.Title,
                row.Status.ToString(),
                row.Priority.ToString(),
                row.OccurrenceState.ToString(),
                row.DueUtc,
                row.CreatedUtc,
                row.CompletedUtc))
            .ToList();

        return new GetScheduleWorkOrdersResponse(scheduleId, items);
    }
}
