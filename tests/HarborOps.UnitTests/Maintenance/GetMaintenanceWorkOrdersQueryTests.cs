using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetMaintenanceWorkOrdersQueryTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetWorkOrdersAsync_returns_first_page_using_default_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        for (var index = 1; index <= 26; index++)
        {
            await workOrders.AddAsync(MaintenanceWorkOrder.Create(
                vesselId,
                $"Work order {index:D2}",
                "Routine task",
                WorkOrderPriority.Medium,
                FixedNow.AddDays(index).UtcDateTime));
        }
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(26, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Work order 01", response.Items[0].Title);
        Assert.Equal("Work order 25", response.Items[^1].Title);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_applies_custom_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        for (var index = 1; index <= 5; index++)
        {
            await workOrders.AddAsync(MaintenanceWorkOrder.Create(
                vesselId,
                $"Work order {index:D2}",
                null,
                WorkOrderPriority.Medium,
                FixedNow.AddDays(index).UtcDateTime));
        }
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal("Work order 03", response.Items[0].Title);
        Assert.Equal("Work order 04", response.Items[1].Title);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_orders_by_due_date_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var sameDueDate = FixedNow.AddDays(3).UtcDateTime;
        var first = MaintenanceWorkOrder.Create(vesselId, "Tied A", null, WorkOrderPriority.Low, sameDueDate);
        var second = MaintenanceWorkOrder.Create(vesselId, "Tied B", null, WorkOrderPriority.Low, sameDueDate);
        var earliest = MaintenanceWorkOrder.Create(vesselId, "Earliest", null, WorkOrderPriority.Low, FixedNow.AddDays(1).UtcDateTime);

        await workOrders.AddRangeAsync(first, second, earliest);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest(),
            CancellationToken.None);

        var expectedTiedOrder = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(earliest.Id, response.Items[0].WorkOrderId);
        Assert.Equal(expectedTiedOrder[0], response.Items[1].WorkOrderId);
        Assert.Equal(expectedTiedOrder[1], response.Items[2].WorkOrderId);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_places_in_progress_work_orders_before_other_statuses()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var open = MaintenanceWorkOrder.Create(
            vesselId,
            "Open first by due date",
            null,
            WorkOrderPriority.Low,
            FixedNow.AddDays(1).UtcDateTime);
        var inProgress = MaintenanceWorkOrder.Create(
            vesselId,
            "In progress despite later due date",
            null,
            WorkOrderPriority.Low,
            FixedNow.AddDays(7).UtcDateTime);
        inProgress.Start();

        await dbContext.Set<MaintenanceWorkOrder>().AddRangeAsync(open, inProgress);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest(),
            CancellationToken.None);

        Assert.Equal(inProgress.Id, response.Items[0].WorkOrderId);
        Assert.Equal(open.Id, response.Items[1].WorkOrderId);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_filters_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var target = MaintenanceWorkOrder.Create(targetVesselId, "Target", null, WorkOrderPriority.Medium, FixedNow.AddDays(1).UtcDateTime);
        var other = MaintenanceWorkOrder.Create(otherVesselId, "Other", null, WorkOrderPriority.Medium, FixedNow.AddDays(1).UtcDateTime);
        await workOrders.AddRangeAsync(target, other);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { VesselId = targetVesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(target.Id, Assert.Single(response.Items).WorkOrderId);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var open = MaintenanceWorkOrder.Create(vesselId, "Open order", null, WorkOrderPriority.Medium, FixedNow.AddDays(1).UtcDateTime);
        var cancelled = MaintenanceWorkOrder.Create(vesselId, "Cancelled order", null, WorkOrderPriority.Medium, FixedNow.AddDays(1).UtcDateTime);
        cancelled.Cancel();
        await workOrders.AddRangeAsync(open, cancelled);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { Status = "Canceled" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(cancelled.Id, Assert.Single(response.Items).WorkOrderId);
        Assert.Equal("Canceled", response.Items[0].Status);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_filters_by_priority()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var critical = MaintenanceWorkOrder.Create(vesselId, "Critical order", null, WorkOrderPriority.Critical, FixedNow.AddDays(1).UtcDateTime);
        var low = MaintenanceWorkOrder.Create(vesselId, "Low order", null, WorkOrderPriority.Low, FixedNow.AddDays(1).UtcDateTime);
        await workOrders.AddRangeAsync(critical, low);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { Priority = "Critical" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(critical.Id, Assert.Single(response.Items).WorkOrderId);
        Assert.Equal("Critical", response.Items[0].Priority);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_filters_overdue_true_excludes_completed_and_future()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var overdueOpen = MaintenanceWorkOrder.Create(vesselId, "Overdue open", null, WorkOrderPriority.Medium, FixedNow.AddDays(-1).UtcDateTime);
        var overdueCompleted = MaintenanceWorkOrder.Create(vesselId, "Overdue completed", null, WorkOrderPriority.Medium, FixedNow.AddDays(-2).UtcDateTime);
        overdueCompleted.Complete(FixedNow.AddDays(-1).UtcDateTime);
        var futureOpen = MaintenanceWorkOrder.Create(vesselId, "Future open", null, WorkOrderPriority.Medium, FixedNow.AddDays(1).UtcDateTime);

        await workOrders.AddRangeAsync(overdueOpen, overdueCompleted, futureOpen);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { Overdue = true },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(overdueOpen.Id, Assert.Single(response.Items).WorkOrderId);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_filters_overdue_false_includes_future_and_completed_past_due()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var overdueOpen = MaintenanceWorkOrder.Create(vesselId, "Overdue open", null, WorkOrderPriority.Medium, FixedNow.AddDays(-1).UtcDateTime);
        var overdueCompleted = MaintenanceWorkOrder.Create(vesselId, "Overdue completed", null, WorkOrderPriority.Medium, FixedNow.AddDays(-2).UtcDateTime);
        overdueCompleted.Complete(FixedNow.AddDays(-1).UtcDateTime);
        var futureOpen = MaintenanceWorkOrder.Create(vesselId, "Future open", null, WorkOrderPriority.Medium, FixedNow.AddDays(1).UtcDateTime);

        await workOrders.AddRangeAsync(overdueOpen, overdueCompleted, futureOpen);
        await dbContext.SaveChangesAsync();

        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { Overdue = false },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Contains(response.Items, item => item.WorkOrderId == overdueCompleted.Id);
        Assert.Contains(response.Items, item => item.WorkOrderId == futureOpen.Id);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_returns_empty_when_no_work_orders_match()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var response = await query.GetWorkOrdersAsync(
            new GetMaintenanceWorkOrdersRequest { VesselId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(1, response.PageNumber);
        Assert.Equal(GetMaintenanceWorkOrdersValidator.DefaultPageSize, response.PageSize);
    }
    [Fact]
    public async Task GetWorkOrdersAsync_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<GetMaintenanceWorkOrdersValidationException>(
            () => query.GetWorkOrdersAsync(
                new GetMaintenanceWorkOrdersRequest { VesselId = Guid.Empty },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceWorkOrdersValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_rejects_invalid_status()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<GetMaintenanceWorkOrdersValidationException>(
            () => query.GetWorkOrdersAsync(
                new GetMaintenanceWorkOrdersRequest { Status = "Urgent" },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceWorkOrdersValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_rejects_invalid_priority()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<GetMaintenanceWorkOrdersValidationException>(
            () => query.GetWorkOrdersAsync(
                new GetMaintenanceWorkOrdersRequest { Priority = "Urgent" },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceWorkOrdersValidator.PriorityField, exception.Errors);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<GetMaintenanceWorkOrdersValidationException>(
            () => query.GetWorkOrdersAsync(
                new GetMaintenanceWorkOrdersRequest { PageSize = GetMaintenanceWorkOrdersValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceWorkOrdersValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetWorkOrdersAsync_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetMaintenanceWorkOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));

        var exception = await Assert.ThrowsAsync<GetMaintenanceWorkOrdersValidationException>(
            () => query.GetWorkOrdersAsync(
                new GetMaintenanceWorkOrdersRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(GetMaintenanceWorkOrdersValidator.PageNumberField, exception.Errors);
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
