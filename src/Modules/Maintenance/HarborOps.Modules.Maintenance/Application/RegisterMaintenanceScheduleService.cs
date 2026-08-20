using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class RegisterMaintenanceScheduleService(DbContext dbContext)
{
    public async Task<RegisterMaintenanceScheduleResponse> RegisterAsync(
        RegisterMaintenanceScheduleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterMaintenanceScheduleValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterMaintenanceScheduleValidationException(errors);
        }

        var equipmentIds = request.EquipmentIds!.ToList();

        var equipment = await dbContext.Set<Equipment>()
            .AsNoTracking()
            .Where(item => equipmentIds.Contains(item.Id))
            .Select(item => new { item.Id, item.VesselId })
            .ToListAsync(cancellationToken);

        var missingEquipmentIds = equipmentIds
            .Except(equipment.Select(item => item.Id))
            .ToList();

        if (missingEquipmentIds.Count > 0)
        {
            throw new MaintenanceScheduleEquipmentNotFoundException(missingEquipmentIds);
        }

        var vesselIds = equipment
            .Select(item => item.VesselId)
            .Distinct()
            .ToList();

        if (vesselIds.Count > 1)
        {
            throw new MaintenanceScheduleVesselConflictException(vesselIds);
        }

        var schedule = MaintenanceSchedule.Create(
            request.MaintenanceType!.Trim(),
            Enum.Parse<MaintenanceFrequency>(request.Frequency!.Trim(), ignoreCase: true),
            request.NextDueDate,
            request.NextDueHours);

        dbContext.Set<MaintenanceSchedule>().Add(schedule);

        foreach (var equipmentId in equipmentIds)
        {
            dbContext.Set<ScheduleEquipment>().Add(ScheduleEquipment.Create(schedule.Id, equipmentId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterMaintenanceScheduleResponse(schedule.Id);
    }
}
