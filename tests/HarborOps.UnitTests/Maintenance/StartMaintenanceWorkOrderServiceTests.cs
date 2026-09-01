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
