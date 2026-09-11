using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class StartMaintenanceWorkOrderServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task StartAsync_moves_an_open_work_order_to_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);

        var response = await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal("InProgress", response.Status);
        Assert.Equal(WorkOrderStatus.InProgress, stored.Status);
    }

    [Fact]
    public async Task StartAsync_reports_the_actions_left_after_the_transition()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);

        var response = await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(["Cancel", "ReplaceEquipment"], response.AllowedActions);
        Assert.Null(response.CompletedUtc);
    }

    [Fact]
    public async Task StartAsync_stamps_the_transition_from_the_time_provider()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);

        var response = await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(UtcNow, response.TransitionedUtc);
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task StartAsync_rejects_a_work_order_that_is_not_open(WorkOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, status);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorkOrderTransitionNotAllowedException>(
            () => CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None));

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(WorkOrderStatus.InProgress, exception.RequestedStatus);
    }

    [Fact]
    public async Task A_rejected_start_persists_nothing()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Canceled);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorkOrderTransitionNotAllowedException>(
            () => CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None));

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(WorkOrderStatus.Canceled, stored.Status);
    }

    [Fact]
    public async Task StartAsync_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateService(dbContext).StartAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
    }

    [Fact]
    public async Task StartAsync_leaves_other_work_orders_untouched()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);
        var other = await SeedAsync(dbContext, WorkOrderStatus.Open);

        await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        var stored = await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == other.Id);

        Assert.Equal(WorkOrderStatus.Open, stored.Status);
    }

    [Fact]
    public async Task StartAsync_accepts_a_work_order_whose_reservations_are_fully_covered()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);
        await SeedReservationAsync(dbContext, workOrder.Id, requested: 4m, available: 10m);

        var response = await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal("InProgress", response.Status);
    }

    [Fact]
    public async Task StartAsync_rejects_a_work_order_with_a_reservation_shortage()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);
        var covered = await SeedReservationAsync(dbContext, workOrder.Id, 2m, available: 10m);
        var uncovered = await SeedReservationAsync(dbContext, workOrder.Id, 5m, available: 1m);

        var exception = await Assert.ThrowsAsync<WorkOrderPartReservationShortageException>(
            () => CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None));

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal([uncovered.InventoryItemId], exception.InventoryItemIds);
        Assert.DoesNotContain(covered.InventoryItemId, exception.InventoryItemIds);
    }

    [Fact]
    public async Task A_start_blocked_by_a_shortage_persists_nothing()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);
        await SeedReservationAsync(dbContext, workOrder.Id, requested: 5m, available: 1m);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorkOrderPartReservationShortageException>(
            () => CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None));

        var stored = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(WorkOrderStatus.Open, stored.Status);
    }

    [Fact]
    public async Task A_shortage_on_another_work_order_does_not_block_the_start()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedAsync(dbContext, WorkOrderStatus.Open);
        var other = await SeedAsync(dbContext, WorkOrderStatus.Open);
        await SeedReservationAsync(dbContext, other.Id, requested: 5m, available: 1m);

        var response = await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal("InProgress", response.Status);
    }

    [Fact]
    public async Task StartAsync_starts_a_generated_work_order_and_keeps_its_occurrence_generated()
    {
        await using var dbContext = CreateDbContext();
        var dueDate = DateOnly.FromDateTime(UtcNow).AddDays(5);
        var schedule = MaintenanceSchedule.Create("Main engine overhaul", MaintenanceFrequency.Monthly, dueDate, null);
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            Guid.NewGuid(),
            "Main engine overhaul - due",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate,
            UtcNow);
        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<ScheduleOccurrence>().AddAsync(
            ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, UtcNow));
        await dbContext.SaveChangesAsync();

        var response = await CreateService(dbContext).StartAsync(workOrder.Id, CancellationToken.None);

        var occurrence = await dbContext.Set<ScheduleOccurrence>().AsNoTracking().SingleAsync();
        var storedSchedule = await dbContext.Set<MaintenanceSchedule>().AsNoTracking().SingleAsync();

        Assert.Equal("InProgress", response.Status);
        Assert.Equal(["Cancel", "ReplaceEquipment"], response.AllowedActions);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Null(occurrence.ResolvedUtc);
        Assert.Equal(dueDate, storedSchedule.NextDueDate);
    }

    private static async Task<WorkOrderPartReservation> SeedReservationAsync(
        HarborOpsDbContext dbContext,
        Guid workOrderId,
        decimal requested,
        decimal available)
    {
        var reservation = WorkOrderPartReservation.Create(
            workOrderId,
            Guid.NewGuid(),
            requested,
            available,
            UtcNow);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        return reservation;
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

    private static StartMaintenanceWorkOrderService CreateService(HarborOpsDbContext dbContext)
    {
        return new StartMaintenanceWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
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
}
