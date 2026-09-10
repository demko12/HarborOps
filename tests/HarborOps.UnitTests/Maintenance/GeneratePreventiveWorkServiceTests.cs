using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GeneratePreventiveWorkServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public async Task PreviewAsync_lists_due_occurrences_without_persisting_anything()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);

        var response = await CreateService(dbContext).PreviewAsync(Through(Today.AddDays(10)), CancellationToken.None);

        Assert.Equal("Pending", Assert.Single(response.Items).State);
        Assert.Equal(1, response.EligibleCount);
        Assert.Equal(0, response.SkippedCount);
        Assert.Equal(Today.AddDays(10), response.Through);
        Assert.Equal(FixedNow.UtcDateTime, response.PreviewedUtc);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State != EntityState.Unchanged);
        Assert.False(await dbContext.Set<MaintenanceWorkOrder>().AnyAsync());
        Assert.False(await dbContext.Set<ScheduleOccurrence>().AnyAsync());
        Assert.False(await dbContext.Set<WorkOrderEquipment>().AnyAsync());
    }

    [Fact]
    public async Task PreviewAsync_reports_an_already_generated_occurrence_as_skipped()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        var service = CreateService(dbContext);
        var generated = Assert.Single(
            (await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None)).Results);

        var response = await service.PreviewAsync(Through(Today.AddDays(10)), CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(0, response.EligibleCount);
        Assert.Equal(1, response.SkippedCount);
        Assert.Equal("Generated", item.State);
        Assert.Equal(generated.WorkOrderId, item.WorkOrderId);
        Assert.Single(await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PreviewAsync_rejects_an_invalid_request()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GeneratePreventiveWorkValidationException>(
            () => CreateService(dbContext).PreviewAsync(new GeneratePreventiveWorkRequest(), CancellationToken.None));

        Assert.Contains(GeneratePreventiveWorkValidator.ThroughField, exception.Errors);
    }

    [Fact]
    public async Task GenerateAsync_creates_a_work_order_with_schedule_derived_fields_and_equipment_scope()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Main engine overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            mainEngine.Id,
            boiler.Id);

        var response = await CreateService(dbContext).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);

        var result = Assert.Single(response.Results);
        var workOrder = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();
        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var scope = await dbContext.Set<WorkOrderEquipment>()
            .AsNoTracking()
            .Where(link => link.WorkOrderId == workOrder.Id)
            .Select(link => link.EquipmentId)
            .ToListAsync();

        Assert.Equal(1, response.CreatedCount);
        Assert.Equal(0, response.SkippedCount);
        Assert.Equal(FixedNow.UtcDateTime, response.GeneratedUtc);
        Assert.Equal("Created", result.Outcome);
        Assert.Equal("Generated", result.State);
        Assert.Equal(workOrder.Id, result.WorkOrderId);
        Assert.Equal(occurrence.Id, result.OccurrenceId);

        Assert.Equal(vesselId, workOrder.VesselId);
        Assert.Equal("Main engine overhaul - due 2026-09-15", workOrder.Title);
        Assert.Equal(
            $"Preventive maintenance 'Main engine overhaul' (Monthly) generated from schedule {schedule.Id} for due date 2026-09-15.",
            workOrder.Description);
        Assert.Equal(WorkOrderPriority.Medium, workOrder.Priority);
        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.Equal(new DateTime(2026, 9, 15, 23, 59, 59, DateTimeKind.Utc), workOrder.DueUtc);
        Assert.Equal(schedule.Id, workOrder.SourceScheduleId);
        Assert.Equal(Today.AddDays(5), workOrder.SourceOccurrenceDueDate);
        Assert.Null(workOrder.AssignedEngineerId);
        Assert.Equal(new[] { mainEngine.Id, boiler.Id }.Order(), scope.Order());

        Assert.Equal(schedule.Id, occurrence.ScheduleId);
        Assert.Equal(Today.AddDays(5), occurrence.DueDate);
        Assert.Equal(workOrder.Id, occurrence.WorkOrderId);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(FixedNow.UtcDateTime, occurrence.GeneratedUtc);
    }

    [Fact]
    public async Task GenerateAsync_uses_a_requested_priority()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);

        await CreateService(dbContext).GenerateAsync(
            Through(Today.AddDays(10), priority: "critical"),
            CancellationToken.None);

        var workOrder = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(WorkOrderPriority.Critical, workOrder.Priority);
    }

    [Fact]
    public async Task GenerateAsync_resolves_the_vessel_from_maintenance_equipment_and_filters_by_it()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var other = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Boiler");
        await SeedScheduleAsync(dbContext, "Mine", MaintenanceFrequency.Monthly, Today.AddDays(5), null, mine.Id);
        await SeedScheduleAsync(dbContext, "Other", MaintenanceFrequency.Monthly, Today.AddDays(5), null, other.Id);

        var response = await CreateService(dbContext).GenerateAsync(
            Through(Today.AddDays(10), vesselId),
            CancellationToken.None);

        var result = Assert.Single(response.Results);
        var workOrder = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(vesselId, result.VesselId);
        Assert.Equal(vesselId, workOrder.VesselId);
        Assert.Equal([mine.Id], result.EquipmentIds);
    }

    [Fact]
    public async Task GenerateAsync_creates_every_recurrence_up_to_the_through_date()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Weekly check", MaintenanceFrequency.Weekly, Today, null, equipment.Id);

        var response = await CreateService(dbContext).GenerateAsync(Through(Today.AddDays(14)), CancellationToken.None);

        var workOrders = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync();

        Assert.Equal(3, response.CreatedCount);
        Assert.Equal(
            [Today, Today.AddDays(7), Today.AddDays(14)],
            response.Results.Select(result => result.DueDate));
        Assert.Equal(
            [Today, Today.AddDays(7), Today.AddDays(14)],
            workOrders.Select(workOrder => workOrder.SourceOccurrenceDueDate!.Value).Order());
    }

    [Fact]
    public async Task GenerateAsync_includes_an_overdue_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overdue", MaintenanceFrequency.Monthly, Today.AddDays(-4), null, equipment.Id);

        var response = await CreateService(dbContext).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);

        Assert.Equal(Today.AddDays(-4), Assert.Single(response.Results).DueDate);
    }

    [Fact]
    public async Task GenerateAsync_does_not_advance_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);

        await CreateService(dbContext).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);

        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(Today.AddDays(5), schedule.NextDueDate);
    }

    [Fact]
    public async Task GenerateAsync_is_idempotent_for_a_repeated_request()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        var service = CreateService(dbContext);

        var first = await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);
        var second = await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);

        var created = Assert.Single(first.Results);
        var repeated = Assert.Single(second.Results);

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(1, second.SkippedCount);
        Assert.Equal("Skipped", repeated.Outcome);
        Assert.Equal("Generated", repeated.State);
        Assert.Equal(created.WorkOrderId, repeated.WorkOrderId);
        Assert.Equal(created.OccurrenceId, repeated.OccurrenceId);
        Assert.Single(await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync());
        Assert.Single(await dbContext.Set<ScheduleOccurrence>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_competing_request_that_commits_first_is_reported_as_the_existing_link()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var equipment = await SeedEquipmentAsync(seedContext, Guid.NewGuid(), "Main engine");
            await SeedScheduleAsync(seedContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        }

        GeneratePreventiveWorkResponse first;
        GeneratePreventiveWorkResponse second;

        await using (var firstRequest = CreateDbContext(databaseName))
        {
            first = await CreateService(firstRequest).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);
        }

        await using (var secondRequest = CreateDbContext(databaseName))
        {
            second = await CreateService(secondRequest).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext(databaseName);

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(Assert.Single(first.Results).WorkOrderId, Assert.Single(second.Results).WorkOrderId);
        Assert.Single(await verifyContext.Set<MaintenanceWorkOrder>().ToListAsync());
        Assert.Single(await verifyContext.Set<ScheduleOccurrence>().ToListAsync());
    }

    [Fact]
    public async Task GenerateAsync_reports_a_mixed_batch_of_created_and_skipped_occurrences()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        var existing = await SeedScheduleAsync(
            dbContext,
            "Existing",
            MaintenanceFrequency.Monthly,
            Today.AddDays(2),
            null,
            equipment.Id);
        var service = CreateService(dbContext);
        await service.GenerateAsync(Through(Today.AddDays(3)), CancellationToken.None);
        var added = await SeedScheduleAsync(dbContext, "Added", MaintenanceFrequency.Monthly, Today.AddDays(6), null, equipment.Id);

        var response = await service.GenerateAsync(Through(Today.AddDays(7)), CancellationToken.None);

        Assert.Equal(1, response.CreatedCount);
        Assert.Equal(1, response.SkippedCount);
        Assert.Equal([existing.Id, added.Id], response.Results.Select(result => result.ScheduleId));
        Assert.Equal(["Skipped", "Created"], response.Results.Select(result => result.Outcome));
        Assert.Equal(2, (await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync()).Count);
        Assert.Equal(2, (await dbContext.Set<ScheduleOccurrence>().AsNoTracking().ToListAsync()).Count);
    }

    [Fact]
    public async Task GenerateAsync_skips_a_cancelled_occurrence_instead_of_recreating_it()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            equipment.Id);
        var cancelledWorkOrderId = await SeedCancelledOccurrenceAsync(dbContext, schedule, vesselId, Today.AddDays(5));

        var response = await CreateService(dbContext).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);

        var result = Assert.Single(response.Results);

        Assert.Equal(0, response.CreatedCount);
        Assert.Equal("Skipped", result.Outcome);
        Assert.Equal("Cancelled", result.State);
        Assert.Equal(cancelledWorkOrderId, result.WorkOrderId);
        Assert.Single(await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task GenerateAsync_ignores_hour_only_and_undated_schedules()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Hours only", MaintenanceFrequency.RunningHours, null, 800, equipment.Id);
        await SeedScheduleAsync(
            dbContext,
            "Hours with a date",
            MaintenanceFrequency.RunningHours,
            Today.AddDays(3),
            800,
            equipment.Id);
        await SeedScheduleAsync(dbContext, "Undated", MaintenanceFrequency.Monthly, null, null, equipment.Id);

        var response = await CreateService(dbContext).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None);

        Assert.Empty(response.Results);
        Assert.Equal(0, response.CreatedCount);
        Assert.False(await dbContext.Set<MaintenanceWorkOrder>().AnyAsync());
    }

    [Fact]
    public async Task GenerateAsync_saves_nothing_when_the_batch_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var equipment = await SeedEquipmentAsync(seedContext, Guid.NewGuid(), "Main engine");
            await SeedScheduleAsync(seedContext, "First", MaintenanceFrequency.Monthly, Today.AddDays(2), null, equipment.Id);
            await SeedScheduleAsync(seedContext, "Second", MaintenanceFrequency.Monthly, Today.AddDays(4), null, equipment.Id);
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);

        Assert.Empty(await verifyContext.Set<MaintenanceWorkOrder>().ToListAsync());
        Assert.Empty(await verifyContext.Set<ScheduleOccurrence>().ToListAsync());
        Assert.Empty(await verifyContext.Set<WorkOrderEquipment>().ToListAsync());
    }

    [Fact]
    public async Task GenerateAsync_rejects_a_through_date_before_the_time_provider_day()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overdue", MaintenanceFrequency.Monthly, Today.AddDays(-4), null, equipment.Id);

        var exception = await Assert.ThrowsAsync<GeneratePreventiveWorkValidationException>(
            () => CreateService(dbContext).GenerateAsync(Through(Today.AddDays(-1)), CancellationToken.None));

        Assert.Contains(GeneratePreventiveWorkValidator.ThroughField, exception.Errors);
        Assert.False(await dbContext.Set<MaintenanceWorkOrder>().AnyAsync());
    }

    [Fact]
    public async Task ReplaceAsync_creates_a_replacement_for_a_cancelled_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            equipment.Id);
        var service = CreateService(dbContext);
        var generated = Assert.Single(
            (await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None)).Results);
        await CancelAsync(dbContext, generated.WorkOrderId);

        var response = await service.ReplaceAsync(
            generated.OccurrenceId,
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var replacement = await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .SingleAsync(workOrder => workOrder.Id == response.WorkOrderId);
        var original = await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .SingleAsync(workOrder => workOrder.Id == generated.WorkOrderId);

        Assert.NotEqual(generated.WorkOrderId, response.WorkOrderId);
        Assert.Equal(generated.WorkOrderId, response.ReplacedWorkOrderId);
        Assert.Equal("Generated", response.State);
        Assert.Equal(Today.AddDays(5), response.DueDate);
        Assert.Equal(vesselId, response.VesselId);
        Assert.Equal([equipment.Id], response.EquipmentIds);

        Assert.Equal(generated.OccurrenceId, occurrence.Id);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(response.WorkOrderId, occurrence.WorkOrderId);
        Assert.Equal(generated.WorkOrderId, occurrence.ReplacedWorkOrderId);
        Assert.Null(occurrence.ResolvedUtc);

        Assert.Equal(WorkOrderStatus.Open, replacement.Status);
        Assert.Equal(schedule.Id, replacement.SourceScheduleId);
        Assert.Equal(Today.AddDays(5), replacement.SourceOccurrenceDueDate);
        Assert.Equal(WorkOrderStatus.Canceled, original.Status);
    }

    [Fact]
    public async Task ReplaceAsync_uses_the_current_equipment_scope_of_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            mainEngine.Id);
        var service = CreateService(dbContext);
        var generated = Assert.Single(
            (await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None)).Results);
        await CancelAsync(dbContext, generated.WorkOrderId);
        await dbContext.Set<ScheduleEquipment>().AddAsync(ScheduleEquipment.Create(schedule.Id, boiler.Id));
        await dbContext.SaveChangesAsync();

        var response = await service.ReplaceAsync(
            generated.OccurrenceId,
            new GeneratePreventiveWorkRequest(),
            CancellationToken.None);

        var scope = await dbContext.Set<WorkOrderEquipment>()
            .AsNoTracking()
            .Where(link => link.WorkOrderId == response.WorkOrderId)
            .Select(link => link.EquipmentId)
            .ToListAsync();

        Assert.Equal([boiler.Id, mainEngine.Id], response.EquipmentIds);
        Assert.Equal(new[] { mainEngine.Id, boiler.Id }.Order(), scope.Order());
    }

    [Fact]
    public async Task ReplaceAsync_does_not_advance_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        var service = CreateService(dbContext);
        var generated = Assert.Single(
            (await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None)).Results);
        await CancelAsync(dbContext, generated.WorkOrderId);

        await service.ReplaceAsync(generated.OccurrenceId, new GeneratePreventiveWorkRequest(), CancellationToken.None);

        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(Today.AddDays(5), schedule.NextDueDate);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_an_occurrence_that_is_not_cancelled()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        await SeedScheduleAsync(dbContext, "Overhaul", MaintenanceFrequency.Monthly, Today.AddDays(5), null, equipment.Id);
        var service = CreateService(dbContext);
        var generated = Assert.Single(
            (await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None)).Results);

        var exception = await Assert.ThrowsAsync<PreventiveWorkReplacementNotAllowedException>(
            () => service.ReplaceAsync(generated.OccurrenceId, new GeneratePreventiveWorkRequest(), CancellationToken.None));

        Assert.Equal(generated.OccurrenceId, exception.OccurrenceId);
        Assert.Equal(ScheduleOccurrenceState.Generated, exception.CurrentState);
        Assert.Single(await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReplaceAsync_rejects_an_unknown_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<ScheduleOccurrenceNotFoundException>(
            () => CreateService(dbContext).ReplaceAsync(missingId, new GeneratePreventiveWorkRequest(), CancellationToken.None));

        Assert.Equal(missingId, exception.OccurrenceId);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_a_schedule_that_became_hour_based()
    {
        await using var dbContext = CreateDbContext();
        var equipment = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Main engine");
        var schedule = await SeedScheduleAsync(
            dbContext,
            "Overhaul",
            MaintenanceFrequency.Monthly,
            Today.AddDays(5),
            null,
            equipment.Id);
        var service = CreateService(dbContext);
        var generated = Assert.Single(
            (await service.GenerateAsync(Through(Today.AddDays(10)), CancellationToken.None)).Results);
        await CancelAsync(dbContext, generated.WorkOrderId);
        typeof(MaintenanceSchedule)
            .GetProperty(nameof(MaintenanceSchedule.Frequency))!
            .SetValue(schedule, nameof(MaintenanceFrequency.RunningHours));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotTimeBasedException>(
            () => service.ReplaceAsync(generated.OccurrenceId, new GeneratePreventiveWorkRequest(), CancellationToken.None));

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();

        Assert.Equal(schedule.Id, exception.ScheduleId);
        Assert.Equal(ScheduleOccurrenceState.Cancelled, occurrence.State);
    }

    private static GeneratePreventiveWorkRequest Through(
        DateOnly through,
        Guid? vesselId = null,
        string? priority = null)
    {
        return new GeneratePreventiveWorkRequest
        {
            Through = through,
            VesselId = vesselId,
            Priority = priority,
        };
    }

    private static GeneratePreventiveWorkService CreateService(HarborOpsDbContext dbContext)
    {
        return new GeneratePreventiveWorkService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static async Task CancelAsync(HarborOpsDbContext dbContext, Guid workOrderId)
    {
        await new CancelMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow))
            .CancelAsync(workOrderId, CancellationToken.None);
    }

    private static async Task<Equipment> SeedEquipmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        string name)
    {
        var equipment = Equipment.Register(vesselId, name, "Propulsion", null, null);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.SaveChangesAsync();

        return equipment;
    }

    private static async Task<MaintenanceSchedule> SeedScheduleAsync(
        HarborOpsDbContext dbContext,
        string maintenanceType,
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate,
        int? nextDueHours,
        params Guid[] equipmentIds)
    {
        var schedule = MaintenanceSchedule.Create(maintenanceType, frequency, nextDueDate, nextDueHours);

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);

        foreach (var equipmentId in equipmentIds)
        {
            await dbContext.Set<ScheduleEquipment>().AddAsync(ScheduleEquipment.Create(schedule.Id, equipmentId));
        }

        await dbContext.SaveChangesAsync();

        return schedule;
    }

    private static async Task<Guid> SeedCancelledOccurrenceAsync(
        HarborOpsDbContext dbContext,
        MaintenanceSchedule schedule,
        Guid vesselId,
        DateOnly dueDate)
    {
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            vesselId,
            "Preventive work",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate);
        var occurrence = ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, FixedNow.UtcDateTime);
        workOrder.Cancel();
        occurrence.MarkCancelled(workOrder.Id, FixedNow.UtcDateTime);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<ScheduleOccurrence>().AddAsync(occurrence);
        await dbContext.SaveChangesAsync();

        return workOrder.Id;
    }

    private static HarborOpsDbContext CreateDbContext(
        string? databaseName = null,
        bool failOnSave = false)
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}");

        if (failOnSave)
        {
            builder.AddInterceptors(new FailingSaveChangesInterceptor());
        }

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("The save was rejected.");
        }
    }
}
