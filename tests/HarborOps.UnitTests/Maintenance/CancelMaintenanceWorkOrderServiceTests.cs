using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CancelMaintenanceWorkOrderServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Theory]
    [InlineData(WorkOrderStatus.Open)]
    [InlineData(WorkOrderStatus.InProgress)]
    public async Task CancelAsync_cancels_an_open_or_running_work_order(WorkOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, status);

        var response = await CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None);

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal("Canceled", response.Status);
        Assert.Empty(response.AllowedActions);
        Assert.Equal(WorkOrderStatus.Canceled, stored.Status);
    }

    [Fact]
    public async Task Cancelling_does_not_record_a_completion_timestamp()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.InProgress);

        var response = await CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None);

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Null(response.CompletedUtc);
        Assert.Null(stored.CompletedUtc);
    }

    [Fact]
    public async Task CancelAsync_stamps_the_transition_from_the_time_provider()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);

        var response = await CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(UtcNow, response.TransitionedUtc);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task CancelAsync_rejects_a_closed_work_order(WorkOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, status);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorkOrderTransitionNotAllowedException>(
            () => CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None));

        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(WorkOrderStatus.Canceled, exception.RequestedStatus);
    }

    [Fact]
    public async Task A_rejected_cancellation_keeps_the_completion_record_intact()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Completed);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorkOrderTransitionNotAllowedException>(
            () => CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None));

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(WorkOrderStatus.Completed, stored.Status);
        Assert.Equal(UtcNow, stored.CompletedUtc);
    }

    [Fact]
    public async Task CancelAsync_keeps_the_equipment_scope()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.InProgress);
        var equipment = Equipment.Register(workOrder.VesselId, "Main engine", "Propulsion", null, null);
        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.Set<WorkOrderEquipment>()
            .AddAsync(WorkOrderEquipment.Create(workOrder.Id, equipment.Id));
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None);

        var link = Assert.Single(await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync());
        Assert.Equal(equipment.Id, link.EquipmentId);
    }

    [Fact]
    public async Task CancelAsync_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateService(dbContext).CancelAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
    }

    [Fact]
    public async Task CancelAsync_marks_a_generated_occurrence_cancelled_without_advancing_the_schedule()
    {
        await using var dbContext = CreateDbContext();
        var (schedule, workOrder) = await SeedGeneratedAsync(dbContext, WorkOrderStatus.InProgress);
        var nextDueDate = schedule.NextDueDate;

        var response = await CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var storedSchedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal("Canceled", response.Status);
        Assert.Equal(ScheduleOccurrenceState.Cancelled, occurrence.State);
        Assert.Equal(workOrder.Id, occurrence.WorkOrderId);
        Assert.Equal(UtcNow, occurrence.ResolvedUtc);
        Assert.Equal(nextDueDate, storedSchedule.NextDueDate);
    }

    [Fact]
    public async Task Cancelling_a_manual_work_order_leaves_generated_occurrences_untouched()
    {
        await using var dbContext = CreateDbContext();
        await SeedGeneratedAsync(dbContext, WorkOrderStatus.Open);
        var manual = await SeedAsync(dbContext, WorkOrderStatus.Open);

        await CreateService(dbContext).CancelAsync(manual.Id, CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();

        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Null(occurrence.ResolvedUtc);
    }

    [Fact]
    public async Task A_rejected_cancellation_keeps_the_completed_occurrence_of_a_generated_work_order()
    {
        await using var dbContext = CreateDbContext();
        var (_, workOrder) = await SeedGeneratedAsync(dbContext, WorkOrderStatus.Completed);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorkOrderTransitionNotAllowedException>(
            () => CreateService(dbContext).CancelAsync(workOrder.Id, CancellationToken.None));

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();

        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
    }

    private static async Task<MaintenanceWorkOrder> SeedAsync(
        HarborOpsDbContext dbContext,
        WorkOrderStatus status)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            UtcNow.AddDays(7));

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(UtcNow);
                break;

            case WorkOrderStatus.Canceled:
                workOrder.Cancel();
                break;

            default:
                break;
        }

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder;
    }

    private static CancelMaintenanceWorkOrderService CreateService(HarborOpsDbContext dbContext)
    {
        return new CancelMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
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
    WorkOrderStatus status)
    {
        var dueDate = DateOnly.FromDateTime(UtcNow).AddDays(5);
        var equipment = Equipment.Register(Guid.NewGuid(), "Main engine", "Propulsion", null, null);
        var schedule = MaintenanceSchedule.Create("Main engine overhaul", MaintenanceFrequency.Monthly, dueDate, null);
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            equipment.VesselId,
            "Main engine overhaul - due",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate);
        var occurrence = ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, UtcNow);

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Complete(UtcNow);
                occurrence.MarkCompleted(workOrder.Id, UtcNow);
                break;

            default:
                break;
        }

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
        await dbContext.Set<ScheduleEquipment>().AddAsync(ScheduleEquipment.Create(schedule.Id, equipment.Id));
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<WorkOrderEquipment>().AddAsync(WorkOrderEquipment.Create(workOrder.Id, equipment.Id));
        await dbContext.Set<ScheduleOccurrence>().AddAsync(occurrence);
        await dbContext.SaveChangesAsync();

        return (schedule, workOrder);
    }
}
