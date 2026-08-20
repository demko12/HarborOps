using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ReportsMaintenanceBacklogQueryTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 15);
    private static readonly DateTime AsOfMidnightUtc = AsOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    [Fact]
    public async Task SearchAsync_returns_empty_result_when_no_work_orders_match()
    {
        await using var dbContext = CreateDbContext();

        var result = await QueryAsync(dbContext, AsOf, vesselId: Guid.NewGuid());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_filters_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var target = MaintenanceWorkOrder.Create(
            targetVesselId, "Target", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(1));
        var other = MaintenanceWorkOrder.Create(
            otherVesselId, "Other", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(1));
        await dbContext.AddRangeAsync(target, other);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, vesselId: targetVesselId);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(target.Id, Assert.Single(result.Items).WorkOrderId);
    }

    [Fact]
    public async Task SearchAsync_filters_by_priority()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var critical = MaintenanceWorkOrder.Create(
            vesselId, "Critical", null, WorkOrderPriority.Critical, AsOfMidnightUtc.AddDays(1));
        var low = MaintenanceWorkOrder.Create(
            vesselId, "Low", null, WorkOrderPriority.Low, AsOfMidnightUtc.AddDays(1));
        await dbContext.AddRangeAsync(critical, low);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, priority: "Critical");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(critical.Id, Assert.Single(result.Items).WorkOrderId);
        Assert.Equal("Critical", result.Items[0].Priority);
    }

    [Fact]
    public async Task SearchAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var open = MaintenanceWorkOrder.Create(
            vesselId, "Open order", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(1));
        var canceled = MaintenanceWorkOrder.Create(
            vesselId, "Canceled order", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(1));
        canceled.Cancel();
        await dbContext.AddRangeAsync(open, canceled);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, status: "Canceled");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(canceled.Id, Assert.Single(result.Items).WorkOrderId);
        Assert.Equal("Canceled", result.Items[0].Status);
    }

    [Fact]
    public async Task SearchAsync_includes_due_dates_on_range_boundaries_and_excludes_outside()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        var onFrom = MaintenanceWorkOrder.Create(
            vesselId, "ON-FROM", null, WorkOrderPriority.Medium, from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var onTo = MaintenanceWorkOrder.Create(
            vesselId, "ON-TO", null, WorkOrderPriority.Medium, to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var beforeFrom = MaintenanceWorkOrder.Create(
            vesselId, "BEFORE-FROM", null, WorkOrderPriority.Medium, from.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var afterTo = MaintenanceWorkOrder.Create(
            vesselId, "AFTER-TO", null, WorkOrderPriority.Medium, to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        await dbContext.AddRangeAsync(onFrom, onTo, beforeFrom, afterTo);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, dueFrom: from, dueTo: to);

        Assert.Equal(2, result.TotalCount);
        var ids = result.Items.Select(item => item.WorkOrderId).ToArray();
        Assert.Contains(onFrom.Id, ids);
        Assert.Contains(onTo.Id, ids);
    }

    [Fact]
    public async Task SearchAsync_overdue_true_excludes_completed_and_future_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var overdueOpen = MaintenanceWorkOrder.Create(
            vesselId, "Overdue open", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(-1));
        var overdueCompleted = MaintenanceWorkOrder.Create(
            vesselId, "Overdue completed", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(-2));
        overdueCompleted.Complete(AsOfMidnightUtc.AddDays(-1));
        var futureOpen = MaintenanceWorkOrder.Create(
            vesselId, "Future open", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(1));
        await dbContext.AddRangeAsync(overdueOpen, overdueCompleted, futureOpen);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, overdue: true);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(overdueOpen.Id, Assert.Single(result.Items).WorkOrderId);
        Assert.True(result.Items[0].IsOverdue);
    }

    [Fact]
    public async Task SearchAsync_work_order_due_exactly_on_as_of_date_is_not_overdue()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var dueOnAsOf = MaintenanceWorkOrder.Create(vesselId, "Due today", null, WorkOrderPriority.Medium, AsOfMidnightUtc);
        await dbContext.AddAsync(dueOnAsOf);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var item = Assert.Single(result.Items);
        Assert.False(item.IsOverdue);
    }

    [Fact]
    public async Task SearchAsync_overdue_false_includes_future_and_completed_past_due_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var overdueOpen = MaintenanceWorkOrder.Create(
            vesselId, "Overdue open", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(-1));
        var overdueCompleted = MaintenanceWorkOrder.Create(
            vesselId, "Overdue completed", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(-2));
        overdueCompleted.Complete(AsOfMidnightUtc.AddDays(-1));
        var futureOpen = MaintenanceWorkOrder.Create(
            vesselId, "Future open", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(1));
        await dbContext.AddRangeAsync(overdueOpen, overdueCompleted, futureOpen);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, overdue: false);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.WorkOrderId == overdueCompleted.Id);
        Assert.Contains(result.Items, item => item.WorkOrderId == futureOpen.Id);
    }

    [Fact]
    public async Task SearchAsync_orders_by_due_date_then_work_order_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var sameDueDate = AsOfMidnightUtc.AddDays(3);
        var first = MaintenanceWorkOrder.Create(vesselId, "Tied A", null, WorkOrderPriority.Low, sameDueDate);
        var second = MaintenanceWorkOrder.Create(vesselId, "Tied B", null, WorkOrderPriority.Low, sameDueDate);
        var earliest = MaintenanceWorkOrder.Create(vesselId, "Earliest", null, WorkOrderPriority.Low, AsOfMidnightUtc.AddDays(1));
        await dbContext.AddRangeAsync(first, second, earliest);
        await dbContext.SaveChangesAsync();

        var expectedTieOrder = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();

        var result = await QueryAsync(dbContext, AsOf);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(earliest.Id, result.Items[0].WorkOrderId);
        Assert.Equal(expectedTieOrder[0], result.Items[1].WorkOrderId);
        Assert.Equal(expectedTieOrder[1], result.Items[2].WorkOrderId);
    }

    [Fact]
    public async Task SearchAsync_returns_second_page_and_total_count_across_all_pages()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        for (var index = 1; index <= 26; index++)
        {
            await dbContext.AddAsync(MaintenanceWorkOrder.Create(
                vesselId, $"Work order {index:D2}", null, WorkOrderPriority.Medium, AsOfMidnightUtc.AddDays(index)));
        }
        await dbContext.SaveChangesAsync();

        var secondPage = await QueryAsync(dbContext, AsOf, pageNumber: 2);

        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(25, secondPage.PageSize);
        Assert.Equal(26, secondPage.TotalCount);
        Assert.Single(secondPage.Items);
    }

    private static Task<ReportsMaintenanceBacklogResult> QueryAsync(
        HarborOpsDbContext dbContext,
        DateOnly asOf,
        Guid? vesselId = null,
        string? priority = null,
        string? status = null,
        bool? overdue = null,
        DateOnly? dueFrom = null,
        DateOnly? dueTo = null,
        int pageNumber = 1) => new ReportsMaintenanceBacklogQuery(dbContext).SearchAsync(
            new ReportsMaintenanceBacklogRequest
            {
                AsOf = asOf,
                VesselId = vesselId,
                Priority = priority,
                Status = status,
                Overdue = overdue,
                DueFrom = dueFrom,
                DueTo = dueTo,
                PageNumber = pageNumber,
            },
            CancellationToken.None);

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
