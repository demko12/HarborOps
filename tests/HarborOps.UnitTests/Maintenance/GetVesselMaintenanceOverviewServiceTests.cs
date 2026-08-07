using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetVesselMaintenanceOverviewServiceTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var service = new GetVesselMaintenanceOverviewService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new VesselMaintenanceOverviewRequest
        {
            VesselIds = [Guid.NewGuid(), Guid.Empty],
        };

        var exception = await Assert.ThrowsAsync<GetVesselMaintenanceOverviewValidationException>(
            () => service.GetVesselMaintenanceOverviewAsync(request, CancellationToken.None));

        Assert.Contains(GetVesselMaintenanceOverviewValidator.VesselIdsField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_more_than_100_distinct_vessel_ids()
    {
        await using var dbContext = CreateDbContext();
        var service = new GetVesselMaintenanceOverviewService(dbContext, new FixedTimeProvider(FixedNow));
        var vesselIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var request = new VesselMaintenanceOverviewRequest
        {
            VesselIds = vesselIds,
        };
        var exception = await Assert.ThrowsAsync<GetVesselMaintenanceOverviewValidationException>(
            () => service.GetVesselMaintenanceOverviewAsync(request, CancellationToken.None));

        Assert.Contains(GetVesselMaintenanceOverviewValidator.VesselIdsField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_calculates_open_overdue_critical_and_next_due()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();

        var overdueLow = MaintenanceWorkOrder.Create(
            vesselId, "Overdue Low",
            "This is an overdue low priority work order.",
            WorkOrderPriority.Low,
            FixedNow.AddDays(-1).UtcDateTime);
        var overdueCritical = MaintenanceWorkOrder.Create(
            vesselId, "Overdue Critical",
            "This is an overdue critical priority work order.",
            WorkOrderPriority.Critical,
            FixedNow.AddDays(-2).UtcDateTime);
        var upcoming = MaintenanceWorkOrder.Create(
            vesselId, "Upcoming Medium",
            "This is an upcoming medium priority work order.",
            WorkOrderPriority.Medium,
            FixedNow.AddDays(1).UtcDateTime);
        var soonest = MaintenanceWorkOrder.Create(
            vesselId, "Soonest High",
            "This is the soonest high priority work order.",
            WorkOrderPriority.High,
            FixedNow.AddHours(1).UtcDateTime);
        var completed = MaintenanceWorkOrder.Create(
            vesselId, "Completed",
            "This work order is completed and should not be counted.",
            WorkOrderPriority.Medium,
            FixedNow.AddDays(-3).UtcDateTime);
        completed.Complete();
        var cancelled = MaintenanceWorkOrder.Create(
            vesselId, "Cancelled",
            "This work order is cancelled and should not be counted.",
            WorkOrderPriority.Medium,
            FixedNow.AddDays(-4).UtcDateTime);
        cancelled.Cancel();
        await workOrders.AddRangeAsync(overdueLow, overdueCritical, upcoming, soonest, completed, cancelled);
        await dbContext.SaveChangesAsync();

        var service = new GetVesselMaintenanceOverviewService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new VesselMaintenanceOverviewRequest { VesselIds = [vesselId] };

        var response = await service.GetVesselMaintenanceOverviewAsync(request, CancellationToken.None);
        var summary = Assert.Single(response.Summaries);
        Assert.Equal(WorkOrderStatus.Completed, completed.Status);
        Assert.Equal(WorkOrderStatus.Canceled, cancelled.Status);
        Assert.Equal(vesselId, summary.VesselId);
        Assert.Equal(4, summary.OpenWorkOrderCount);
        Assert.Equal(2, summary.OverdueWorkOrderCount);
        Assert.Equal(1, summary.CriticalOpenWorkOrderCount);
        Assert.Equal(soonest.DueUtc, summary.NextWorkOrderDueUtc);
    }

    [Fact]
    public async Task GetAsync_returns_zero_counts_for_vessel_with_no_open_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var service = new GetVesselMaintenanceOverviewService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new VesselMaintenanceOverviewRequest { VesselIds = [vesselId] };
        var response = await service.GetVesselMaintenanceOverviewAsync(request, CancellationToken.None);
        var summary = Assert.Single(response.Summaries);
        Assert.Equal(vesselId, summary.VesselId);
        Assert.Equal(0, summary.OpenWorkOrderCount);
        Assert.Equal(0, summary.OverdueWorkOrderCount);
        Assert.Equal(0, summary.CriticalOpenWorkOrderCount);
        Assert.Null(summary.NextWorkOrderDueUtc);
    }

    [Fact]
    public async Task GetAsync_handles_duplicate_vessel_ids_once()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();
        var workOrder1 = MaintenanceWorkOrder.Create(
            vesselId, "Work Order 1",
            "This is the first work order.",
            WorkOrderPriority.Medium,
            FixedNow.AddDays(1).UtcDateTime);
        var workOrder2 = MaintenanceWorkOrder.Create(
            vesselId, "Work Order 2",
            "This is the second work order.",
            WorkOrderPriority.Critical,
            FixedNow.AddDays(2).UtcDateTime);
        await workOrders.AddRangeAsync(workOrder1, workOrder2);
        await dbContext.SaveChangesAsync();
        var service = new GetVesselMaintenanceOverviewService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new VesselMaintenanceOverviewRequest { VesselIds = [vesselId, vesselId] };
        var response = await service.GetVesselMaintenanceOverviewAsync(request, CancellationToken.None);
        var summary = Assert.Single(response.Summaries);
        Assert.Equal(vesselId, summary.VesselId);
        Assert.Equal(2, summary.OpenWorkOrderCount);
        Assert.Equal(0, summary.OverdueWorkOrderCount);
        Assert.Equal(1, summary.CriticalOpenWorkOrderCount);
        Assert.Equal(workOrder1.DueUtc, summary.NextWorkOrderDueUtc);
    }

    [Fact]
    public async Task GetAsync_returns_summaries_for_valid_request()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrders = dbContext.Set<MaintenanceWorkOrder>();
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(MaintenanceWorkOrder.Create(
        vesselId, "Replace filter", "Routine replacement", WorkOrderPriority.High,
        FixedNow.UtcDateTime.AddDays(3)));

        await dbContext.SaveChangesAsync();

        var service = new GetVesselMaintenanceOverviewService(dbContext, new FixedTimeProvider(FixedNow));
        var request = new VesselMaintenanceOverviewRequest { VesselIds = [vesselId] };
        var response = await service.GetVesselMaintenanceOverviewAsync(request, CancellationToken.None);
        var summary = Assert.Single(response.Summaries);

        Assert.Equal(vesselId, summary.VesselId);
        Assert.Equal(1, summary.OpenWorkOrderCount);
        Assert.Equal(0, summary.OverdueWorkOrderCount);
        Assert.Equal(0, summary.CriticalOpenWorkOrderCount);
        Assert.Equal(FixedNow.UtcDateTime.AddDays(3), summary.NextWorkOrderDueUtc);
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
