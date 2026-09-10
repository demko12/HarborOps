using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CompleteMaintenanceWorkOrderService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        Guid workOrderId,
        CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        var currentUtc = timeProvider.GetUtcNow().UtcDateTime;
        var errors = CompleteMaintenanceWorkOrderValidator.Validate(request, currentUtc, workOrder.CreatedUtc);
        if (errors.Count > 0)
        {
            throw new CompleteMaintenanceWorkOrderValidationException(errors);
        }

        var technicianId = request.TechnicianId!.Value;
        var completedUtc = request.CompletedUtc!.Value;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        try
        {
            workOrder.Complete(completedUtc);
        }
        catch (WorkOrderNotCompletableException exception)
        {
            throw new CompleteMaintenanceWorkOrderConflictException(exception.WorkOrderId, exception.CurrentStatus);
        }

        await AdvanceScheduleAsync(workOrder, completedUtc, cancellationToken);

        var maintenanceRecord = MaintenanceRecord.Create
            (workOrder.Id,
            technicianId,
            DateOnly.FromDateTime(completedUtc),
            notes,
            request.Cost,
            request.LaborHours);

        await dbContext.Set<MaintenanceRecord>().AddAsync(maintenanceRecord, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompleteMaintenanceWorkOrderResponse(
            workOrder.Id,
            maintenanceRecord.Id,
            workOrder.CompletedUtc!.Value);
    }

    private async Task AdvanceScheduleAsync(
    MaintenanceWorkOrder workOrder,
    DateTime completedUtc,
    CancellationToken cancellationToken)
    {
        if (workOrder.SourceScheduleId is not { } scheduleId
            || workOrder.SourceOccurrenceDueDate is not { } dueDate)
        {
            return;
        }

        var occurrence = await dbContext.Set<ScheduleOccurrence>()
            .SingleOrDefaultAsync(
                candidate => candidate.ScheduleId == scheduleId && candidate.DueDate == dueDate,
                cancellationToken);

        if (occurrence is null || !occurrence.IsLinkedTo(workOrder.Id))
        {
            throw new PreventiveWorkCompletionLinkInvalidException(workOrder.Id, scheduleId, dueDate);
        }

        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .SingleOrDefaultAsync(candidate => candidate.Id == scheduleId, cancellationToken);

        if (schedule is null)
        {
            throw new MaintenanceScheduleNotFoundException(scheduleId);
        }

        if (!schedule.TryGetFrequency(out var frequency) || !ScheduleOccurrenceCalculator.IsTimeBased(frequency))
        {
            throw new MaintenanceScheduleNotTimeBasedException(schedule.Id, schedule.Frequency);
        }

        try
        {
            occurrence.MarkCompleted(workOrder.Id, completedUtc);
        }
        catch (ScheduleOccurrenceTransitionNotAllowedException)
        {
            throw new PreventiveWorkCompletionLinkInvalidException(workOrder.Id, scheduleId, dueDate);
        }

        schedule.AdvanceAfterOccurrence(frequency, occurrence.DueDate);
    }
}
