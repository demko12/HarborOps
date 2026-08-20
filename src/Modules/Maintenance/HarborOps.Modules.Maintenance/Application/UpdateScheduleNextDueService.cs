using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class UpdateScheduleNextDueService(DbContext dbContext)
{
    public async Task<UpdateScheduleNextDueResponse> UpdateNextDueAsync(
        Guid scheduleId,
        UpdateScheduleNextDueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateScheduleNextDueValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateScheduleNextDueValidationException(errors);
        }

        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .FirstOrDefaultAsync(item => item.Id == scheduleId, cancellationToken);

        if (schedule is null)
        {
            throw new MaintenanceScheduleNotFoundException(scheduleId);
        }

        if (!schedule.TryGetFrequency(out var frequency))
        {
            throw new UpdateScheduleNextDueValidationException(
                UpdateScheduleNextDueValidator.UnsupportedFrequency(schedule.Frequency));
        }

        var frequencyErrors = UpdateScheduleNextDueValidator.ValidateAgainstFrequency(request, frequency);

        if (frequencyErrors.Count > 0)
        {
            throw new UpdateScheduleNextDueValidationException(frequencyErrors);
        }

        schedule.RecordCompletion(frequency, request.CompletedOn!.Value, request.NextDueHours);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateScheduleNextDueResponse(
            schedule.Id,
            schedule.Frequency,
            schedule.NextDueDate,
            schedule.NextDueHours);
    }
}
