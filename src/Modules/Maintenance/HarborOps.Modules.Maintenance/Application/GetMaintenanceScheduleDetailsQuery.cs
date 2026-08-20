using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetMaintenanceScheduleDetailsQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetMaintenanceScheduleDetailsResponse> GetDetailsAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .AsNoTracking()
            .Where(item => item.Id == scheduleId)
            .Select(item => new
            {
                item.Id,
                item.MaintenanceType,
                item.Frequency,
                item.NextDueDate,
                item.NextDueHours,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (schedule is null)
        {
            throw new MaintenanceScheduleNotFoundException(scheduleId);
        }

        var links = dbContext.Set<ScheduleEquipment>().AsNoTracking();

        var equipment = await dbContext.Set<Equipment>()
            .AsNoTracking()
            .Where(item => links.Any(link => link.ScheduleId == scheduleId && link.EquipmentId == item.Id))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new MaintenanceScheduleEquipmentItem(
                item.Id,
                item.VesselId,
                item.Name,
                item.Category,
                item.Manufacturer,
                item.SerialNumber))
            .ToListAsync(cancellationToken);

        return new GetMaintenanceScheduleDetailsResponse(
            schedule.Id,
            equipment.Count > 0 ? equipment[0].VesselId : Guid.Empty,
            schedule.MaintenanceType,
            schedule.Frequency,
            schedule.NextDueDate,
            schedule.NextDueHours,
            MaintenanceSchedule.IsOverdue(schedule.NextDueDate, today),
            MaintenanceSchedule.DaysUntilDue(schedule.NextDueDate, today),
            equipment);
    }
}
