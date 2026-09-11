using System.Data;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GeneratePreventiveWorkService(DbContext dbContext, TimeProvider timeProvider)
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int MaxAttempts = 2;

    private const string CreatedOutcome = "Created";
    private const string SkippedOutcome = "Skipped";

    public async Task<PreviewPreventiveWorkResponse> PreviewAsync(
        GeneratePreventiveWorkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var errors = GeneratePreventiveWorkValidator.Validate(request, today);
        if (errors.Count > 0)
        {
            throw new GeneratePreventiveWorkValidationException(errors);
        }

        var through = request.Through!.Value;

        var occurrences = await PreventiveWorkOccurrenceReader.ReadAsync(
            dbContext,
            today,
            through,
            request.VesselId,
            scheduleId: null,
            cancellationToken);

        var items = occurrences
            .Select(occurrence => occurrence.ToItem(today))
            .ToList();

        var eligibleCount = occurrences.Count(occurrence =>
            occurrence.State is ScheduleOccurrenceState.Pending);

        return new PreviewPreventiveWorkResponse(
            items,
            through,
            eligibleCount,
            occurrences.Count - eligibleCount,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task<GeneratePreventiveWorkResponse> GenerateAsync(
        GeneratePreventiveWorkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var errors = GeneratePreventiveWorkValidator.Validate(request, today);
        if (errors.Count > 0)
        {
            throw new GeneratePreventiveWorkValidationException(errors);
        }

        var priority = GeneratePreventiveWorkValidator.ResolvePriority(request.Priority);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await GenerateOnceAsync(request, today, priority, cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt < MaxAttempts && IsUniqueViolation(exception))
            {
                DetachPendingChanges();
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                DetachPendingChanges();

                throw new PreventiveWorkGenerationConflictException(exception);
            }
        }
    }

    public async Task<ReplacePreventiveWorkOrderResponse> ReplaceAsync(
        Guid occurrenceId,
        GeneratePreventiveWorkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GeneratePreventiveWorkValidator.ValidateReplacement(request);
        if (errors.Count > 0)
        {
            throw new GeneratePreventiveWorkValidationException(errors);
        }

        var priority = GeneratePreventiveWorkValidator.ResolvePriority(request.Priority);

        var occurrence = await dbContext.Set<ScheduleOccurrence>()
            .SingleOrDefaultAsync(candidate => candidate.Id == occurrenceId, cancellationToken);

        if (occurrence is null)
        {
            throw new ScheduleOccurrenceNotFoundException(occurrenceId);
        }

        if (occurrence.State is not ScheduleOccurrenceState.Cancelled)
        {
            throw new PreventiveWorkReplacementNotAllowedException(occurrenceId, occurrence.State);
        }

        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == occurrence.ScheduleId, cancellationToken);

        if (schedule is null)
        {
            throw new MaintenanceScheduleNotFoundException(occurrence.ScheduleId);
        }

        if (!schedule.TryGetFrequency(out var frequency) || !ScheduleOccurrenceCalculator.IsTimeBased(frequency))
        {
            throw new MaintenanceScheduleNotTimeBasedException(schedule.Id, schedule.Frequency);
        }

        var scope = await PreventiveWorkOccurrenceReader.ReadScopeAsync(
            dbContext,
            [schedule.Id],
            cancellationToken);

        if (!scope.TryGetValue(schedule.Id, out var scheduleScope))
        {
            throw new PreventiveWorkScopeMissingException(schedule.Id);
        }

        var generatedUtc = timeProvider.GetUtcNow().UtcDateTime;

        var workOrder = CreateWorkOrder(
            schedule.Id,
            schedule.MaintenanceType,
            schedule.Frequency,
            occurrence.DueDate,
            scheduleScope,
            priority,
            generatedUtc);

        occurrence.Replace(workOrder.Id, generatedUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReplacePreventiveWorkOrderResponse(
            occurrence.Id,
            occurrence.ScheduleId,
            scheduleScope.VesselId,
            occurrence.DueDate,
            PreventiveWorkDueTime.ForDueDate(occurrence.DueDate),
            occurrence.State.ToString(),
            occurrence.WorkOrderId,
            occurrence.ReplacedWorkOrderId,
            scheduleScope.EquipmentIds,
            occurrence.GeneratedUtc);
    }

    private async Task<GeneratePreventiveWorkResponse> GenerateOnceAsync(
        GeneratePreventiveWorkRequest request,
        DateOnly today,
        WorkOrderPriority priority,
        CancellationToken cancellationToken)
    {
        var through = request.Through!.Value;

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var occurrences = await PreventiveWorkOccurrenceReader.ReadAsync(
            dbContext,
            today,
            through,
            request.VesselId,
            scheduleId: null,
            cancellationToken);

        var generatedUtc = timeProvider.GetUtcNow().UtcDateTime;
        var results = new List<PreventiveWorkGenerationResultItem>(occurrences.Count);
        var createdCount = 0;
        var skippedCount = 0;

        foreach (var occurrence in occurrences)
        {
            if (occurrence.State is not ScheduleOccurrenceState.Pending)
            {
                skippedCount++;

                results.Add(new PreventiveWorkGenerationResultItem(
                    occurrence.ScheduleId,
                    occurrence.OccurrenceId!.Value,
                    occurrence.VesselId,
                    occurrence.MaintenanceType,
                    occurrence.DueDate,
                    occurrence.DueUtc,
                    occurrence.State.ToString(),
                    SkippedOutcome,
                    occurrence.WorkOrderId!.Value,
                    occurrence.EquipmentIds));

                continue;
            }

            var workOrder = CreateWorkOrder(
                occurrence.ScheduleId,
                occurrence.MaintenanceType,
                occurrence.Frequency,
                occurrence.DueDate,
                new PreventiveWorkScheduleScope(occurrence.VesselId, occurrence.EquipmentIds),
                priority,
                generatedUtc);

            var scheduleOccurrence = ScheduleOccurrence.Generate(
                occurrence.ScheduleId,
                occurrence.DueDate,
                workOrder.Id,
                generatedUtc);

            await dbContext.Set<ScheduleOccurrence>().AddAsync(scheduleOccurrence, cancellationToken);

            createdCount++;

            results.Add(new PreventiveWorkGenerationResultItem(
                occurrence.ScheduleId,
                scheduleOccurrence.Id,
                occurrence.VesselId,
                occurrence.MaintenanceType,
                occurrence.DueDate,
                occurrence.DueUtc,
                scheduleOccurrence.State.ToString(),
                CreatedOutcome,
                workOrder.Id,
                occurrence.EquipmentIds));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new GeneratePreventiveWorkResponse(
            results,
            through,
            createdCount,
            skippedCount,
            generatedUtc);
    }

    private MaintenanceWorkOrder CreateWorkOrder(
        Guid scheduleId,
        string maintenanceType,
        string frequency,
        DateOnly dueDate,
        PreventiveWorkScheduleScope scope,
        WorkOrderPriority priority,
        DateTime createdUtc)
    {
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            scope.VesselId,
            PreventiveWorkNaming.Title(maintenanceType, dueDate),
            PreventiveWorkNaming.Description(maintenanceType, frequency, scheduleId, dueDate),
            priority,
            scheduleId,
            dueDate,
            createdUtc);

        dbContext.Set<MaintenanceWorkOrder>().Add(workOrder);

        foreach (var equipmentId in scope.EquipmentIds)
        {
            dbContext.Set<WorkOrderEquipment>().Add(WorkOrderEquipment.Create(workOrder.Id, equipmentId));
        }

        return workOrder;
    }

    private void DetachPendingChanges()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        };
    }
}
