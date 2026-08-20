using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetMaintenanceSchedulesQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetMaintenanceSchedulesResponse> GetSchedulesAsync(
        GetMaintenanceSchedulesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetMaintenanceSchedulesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetMaintenanceSchedulesValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetMaintenanceSchedulesValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetMaintenanceSchedulesValidator.DefaultPageSize;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var links = dbContext.Set<ScheduleEquipment>().AsNoTracking();
        var equipment = dbContext.Set<Equipment>().AsNoTracking();
        var schedules = dbContext.Set<MaintenanceSchedule>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            schedules = schedules.Where(schedule => links.Any(link =>
                link.ScheduleId == schedule.Id
                && equipment.Any(item => item.Id == link.EquipmentId && item.VesselId == vesselId)));
        }

        if (request.EquipmentId is { } equipmentId)
        {
            schedules = schedules.Where(schedule => links.Any(link =>
                link.ScheduleId == schedule.Id && link.EquipmentId == equipmentId));
        }

        if (request.DueFrom is { } dueFrom)
        {
            schedules = schedules.Where(schedule =>
                schedule.NextDueDate != null && schedule.NextDueDate >= dueFrom);
        }

        if (request.DueTo is { } dueTo)
        {
            schedules = schedules.Where(schedule =>
                schedule.NextDueDate != null && schedule.NextDueDate <= dueTo);
        }

        if (request.Overdue is { } overdue)
        {
            schedules = overdue
                ? schedules.Where(schedule => schedule.NextDueDate != null && schedule.NextDueDate < today)
                : schedules.Where(schedule => schedule.NextDueDate == null || schedule.NextDueDate >= today);
        }

        var totalCount = await schedules.CountAsync(cancellationToken);

        var rows = await schedules
            .OrderBy(schedule => schedule.NextDueDate == null)
            .ThenBy(schedule => schedule.NextDueDate)
            .ThenBy(schedule => schedule.MaintenanceType)
            .ThenBy(schedule => schedule.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(schedule => new
            {
                schedule.Id,
                schedule.MaintenanceType,
                schedule.Frequency,
                schedule.NextDueDate,
                schedule.NextDueHours,
                VesselId = equipment
                    .Where(item => links.Any(link =>
                        link.ScheduleId == schedule.Id && link.EquipmentId == item.Id))
                    .Select(item => item.VesselId)
                    .FirstOrDefault(),
                EquipmentCount = links.Count(link => link.ScheduleId == schedule.Id),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new MaintenanceScheduleListItem(
                row.Id,
                row.VesselId,
                row.MaintenanceType,
                row.Frequency,
                row.NextDueDate,
                row.NextDueHours,
                MaintenanceSchedule.IsOverdue(row.NextDueDate, today),
                MaintenanceSchedule.DaysUntilDue(row.NextDueDate, today),
                row.EquipmentCount))
            .ToList();

        return new GetMaintenanceSchedulesResponse(items, pageNumber, pageSize, totalCount);
    }
}
