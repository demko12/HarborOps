using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CompleteMaintenanceWorkOrderServiceTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.UtcNow.AddMinutes(10);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow.UtcDateTime);
    [Fact]
    public async Task CompleteWorkOrderAsync_completes_open_work_order_and_creates_record()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        var response = await service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None);

        var persistedWorkOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(candidate => candidate.Id == workOrder.Id);
        var persistedRecord = await dbContext
            .Set<MaintenanceRecord>()
            .SingleAsync(candidate => candidate.Id == response.MaintenanceRecordId);

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal(WorkOrderStatus.Completed, persistedWorkOrder.Status);
        Assert.Equal(request.CompletedUtc, persistedWorkOrder.CompletedUtc);
        Assert.Equal(request.CompletedUtc, response.CompletedUtc);
        Assert.Equal(workOrder.Id, persistedRecord.WorkOrderId);
        Assert.Equal(request.TechnicianId, persistedRecord.TechnicianId);
        Assert.Equal(DateOnly.FromDateTime(request.CompletedUtc!.Value), persistedRecord.CompletionDate);
        Assert.Equal("Filter replaced successfully.", persistedRecord.Notes);
        Assert.Equal(request.Cost, persistedRecord.Cost);
        Assert.Equal(request.LaborHours, persistedRecord.LaborHours);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_completes_in_progress_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        workOrder.Start();
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        var response = await service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None);

        var persistedWorkOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(candidate => candidate.Id == workOrder.Id);

        Assert.Equal(WorkOrderStatus.Completed, persistedWorkOrder.Status);
        Assert.NotEqual(Guid.Empty, response.MaintenanceRecordId);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.Empty,
            CompletedUtc = FixedNow.UtcDateTime.AddDays(1),
            Cost = -10m,
            LaborHours = 0m,
        };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.TechnicianIdField, exception.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CostField, exception.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.LaborHoursField, exception.Errors);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_completed_utc_before_created_utc()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest() with { CompletedUtc = workOrder.CreatedUtc.AddSeconds(-1) };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_non_utc_completed_utc()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest() with
        {
            CompletedUtc = DateTime.SpecifyKind(FixedNow.UtcDateTime, DateTimeKind.Local),
        };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_completed_utc_in_the_future()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest() with { CompletedUtc = FixedNow.UtcDateTime.AddDays(1) };

        var exception = await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderValidationException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, exception.Errors);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_already_completed_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        workOrder.Complete(FixedNow.UtcDateTime.AddHours(-1));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderConflictException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_canceled_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        workOrder.Cancel();
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderConflictException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None));

        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_throws_not_found_for_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => service.CompleteWorkOrderAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_completes_the_generated_occurrence_and_advances_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var (_, workOrder) = await SeedGeneratedAsync(dbContext, Today, Today);
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
        var request = CreateValidRequest();

        var response = await service.CompleteWorkOrderAsync(workOrder.Id, request, CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
        Assert.Equal(request.CompletedUtc, occurrence.ResolvedUtc);
        Assert.Equal(Today.AddMonths(1), schedule.NextDueDate);
        Assert.True(await dbContext.Set<MaintenanceRecord>()
            .AnyAsync(record => record.Id == response.MaintenanceRecordId));
    }

    [Fact]
    public async Task A_repeated_completion_does_not_advance_the_schedule_again()
    {
        await using var dbContext = CreateDbContext();
        var (_, workOrder) = await SeedGeneratedAsync(dbContext, Today, Today);
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        await service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<CompleteMaintenanceWorkOrderConflictException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None));

        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(Today.AddMonths(1), schedule.NextDueDate);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_advances_from_the_occurrence_due_date_not_the_completion_date()
    {
        await using var dbContext = CreateDbContext();
        var overdue = Today.AddDays(-10);
        var (_, workOrder) = await SeedGeneratedAsync(dbContext, overdue, overdue);
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        await service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None);

        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(overdue.AddMonths(1), schedule.NextDueDate);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_keeps_a_schedule_that_already_moved_past_the_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var (_, workOrder) = await SeedGeneratedAsync(dbContext, Today.AddDays(40), Today);
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        await service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
        Assert.Equal(Today.AddDays(40), schedule.NextDueDate);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_a_generated_work_order_without_its_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var (schedule, workOrder) = await SeedGeneratedAsync(dbContext, Today, Today, seedOccurrence: false);
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<PreventiveWorkCompletionLinkInvalidException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None));

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(schedule.Id, exception.ScheduleId);
        Assert.Equal(Today, exception.DueDate);
        Assert.Equal(WorkOrderStatus.Open, stored.Status);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_an_occurrence_linked_to_another_work_order()
    {
        await using var dbContext = CreateDbContext();
        var (_, workOrder) = await SeedGeneratedAsync(
            dbContext,
            Today,
            Today,
            occurrenceWorkOrderId: Guid.NewGuid());
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        await Assert.ThrowsAsync<PreventiveWorkCompletionLinkInvalidException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None));

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(Today, schedule.NextDueDate);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_rejects_a_schedule_that_became_hour_based()
    {
        await using var dbContext = CreateDbContext();
        var (schedule, workOrder) = await SeedGeneratedAsync(dbContext, Today, Today);
        typeof(MaintenanceSchedule)
            .GetProperty(nameof(MaintenanceSchedule.Frequency))!
            .SetValue(schedule, nameof(MaintenanceFrequency.RunningHours));
        await dbContext.SaveChangesAsync();
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<MaintenanceScheduleNotTimeBasedException>(
            () => service.CompleteWorkOrderAsync(workOrder.Id, CreateValidRequest(), CancellationToken.None));

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();
        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();

        Assert.Equal(schedule.Id, exception.ScheduleId);
        Assert.Equal(WorkOrderStatus.Open, stored.Status);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync());
    }

    [Fact]
    public async Task Completing_a_manual_work_order_does_not_touch_schedules_or_occurrences()
    {
        await using var dbContext = CreateDbContext();
        await SeedGeneratedAsync(dbContext, Today, Today);
        var manual = MaintenanceWorkOrder.Create(
            Guid.NewGuid(), "Replace filter", null, WorkOrderPriority.High,
            FixedNow.UtcDateTime.AddDays(3));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(manual);
        await dbContext.SaveChangesAsync();
        var service = new CompleteMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));

        await service.CompleteWorkOrderAsync(manual.Id, CreateValidRequest(), CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var schedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Equal(Today, schedule.NextDueDate);
    }

    private static CompleteMaintenanceWorkOrderRequest CreateValidRequest()
    {
        return new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.NewGuid(),
            CompletedUtc = FixedNow.UtcDateTime,
            Notes = " Filter replaced successfully. ",
            Cost = 150.50m,
            LaborHours = 2.5m,
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static async Task<(MaintenanceSchedule Schedule, MaintenanceWorkOrder WorkOrder)> SeedGeneratedAsync(
    HarborOpsDbContext dbContext,
    DateOnly scheduleNextDueDate,
    DateOnly occurrenceDueDate,
    bool seedOccurrence = true,
    Guid? occurrenceWorkOrderId = null)
    {
        var equipment = Equipment.Register(Guid.NewGuid(), "Main engine", "Propulsion", null, null);
        var schedule = MaintenanceSchedule.Create(
            "Main engine overhaul",
            MaintenanceFrequency.Monthly,
            scheduleNextDueDate,
            null);
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            equipment.VesselId,
            "Main engine overhaul - due",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            occurrenceDueDate);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
        await dbContext.Set<ScheduleEquipment>().AddAsync(ScheduleEquipment.Create(schedule.Id, equipment.Id));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);

        if (seedOccurrence)
        {
            await dbContext.Set<ScheduleOccurrence>().AddAsync(ScheduleOccurrence.Generate(
                schedule.Id,
                occurrenceDueDate,
                occurrenceWorkOrderId ?? workOrder.Id,
                FixedNow.UtcDateTime));
        }

        await dbContext.SaveChangesAsync();

        return (schedule, workOrder);
    }
}
