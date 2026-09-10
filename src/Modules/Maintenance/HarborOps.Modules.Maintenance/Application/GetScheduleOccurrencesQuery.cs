using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetScheduleOccurrencesQuery(DbContext dbContext)
{
    public async Task<GetScheduleOccurrencesResponse> GetOccurrencesAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == scheduleId, cancellationToken);

        if (schedule is null)
        {
            throw new MaintenanceScheduleNotFoundException(scheduleId);
        }

        if (!schedule.TryGetFrequency(out var frequency) || !ScheduleOccurrenceCalculator.IsTimeBased(frequency))
        {
            throw new MaintenanceScheduleNotTimeBasedException(schedule.Id, schedule.Frequency);
        }

        var rows = await dbContext.Set<ScheduleOccurrence>()
            .AsNoTracking()
            .Where(occurrence => occurrence.ScheduleId == scheduleId)
            .OrderBy(occurrence => occurrence.DueDate)
            .ThenBy(occurrence => occurrence.ScheduleId)
            .ThenBy(occurrence => occurrence.Id)
            .Select(occurrence => new
            {
                occurrence.Id,
                occurrence.ScheduleId,
                occurrence.DueDate,
                occurrence.State,
                occurrence.WorkOrderId,
                occurrence.ReplacedWorkOrderId,
                occurrence.GeneratedUtc,
                occurrence.ResolvedUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ScheduleOccurrenceListItem(
                row.Id,
                row.ScheduleId,
                row.DueDate,
                PreventiveWorkDueTime.ForDueDate(row.DueDate),
                row.State.ToString(),
                row.WorkOrderId,
                row.ReplacedWorkOrderId,
                row.GeneratedUtc,
                row.ResolvedUtc))
            .ToList();

        return new GetScheduleOccurrencesResponse(
            schedule.Id,
            schedule.MaintenanceType,
            schedule.Frequency,
            schedule.NextDueDate,
            items);
    }
}
