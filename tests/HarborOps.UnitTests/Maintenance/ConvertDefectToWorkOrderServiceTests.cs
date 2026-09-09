using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ConvertDefectToWorkOrderServiceTests
{
    private static readonly DateTime ObservedAtUtc = new(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReportedAtUtc = new(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly DateTime DueUtc = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ConvertAsync_from_triaged_creates_exactly_one_open_work_order_linked_to_the_defect()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Triaged, vesselId);
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).ConvertAsync(
            defect.Id,
            new ConvertDefectToWorkOrderRequest { DueUtc = DueUtc },
            actorUserId,
            CancellationToken.None);

        // Fresh queries against the same DbContext/in-memory database confirm both the new
        // work order and the updated defect are visible after the single SaveChangesAsync -
        // not just that no exception was thrown.
        var workOrders = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync();
        var storedDefect = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();

        var workOrder = Assert.Single(workOrders);
        Assert.Equal(defect.Id, response.DefectId);
        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.Equal(WorkOrderPriority.High, workOrder.Priority);
        Assert.Equal(vesselId, workOrder.VesselId);
        Assert.Equal("Hydraulic leak on crane boom", workOrder.Title);
        Assert.Equal("Visible oil leak near the boom cylinder", workOrder.Description);
        Assert.Equal(DueUtc, workOrder.DueUtc);

        Assert.Equal(DefectStatus.Converted, storedDefect.Status);
        Assert.Equal(workOrder.Id, storedDefect.WorkOrderId);
        Assert.Equal(actorUserId, storedDefect.ConvertedByUserId);
        Assert.Equal(UtcNow, storedDefect.ConvertedAtUtc);
    }

    [Fact]
    public async Task ConvertAsync_from_reported_throws_and_creates_no_work_order()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Reported, Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<DefectTransitionNotAllowedException>(
            () => CreateService(dbContext).ConvertAsync(
                defect.Id,
                new ConvertDefectToWorkOrderRequest { DueUtc = DueUtc },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(defect.Id, exception.DefectId);
        Assert.Equal(DefectStatus.Reported, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Converted, exception.RequestedStatus);
        Assert.Empty(await dbContext.Set<MaintenanceWorkOrder>().ToListAsync());

        var storedDefect = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();
        Assert.Equal(DefectStatus.Reported, storedDefect.Status);
        Assert.Null(storedDefect.WorkOrderId);
    }

    [Fact]
    public async Task A_second_conversion_attempt_leaves_exactly_one_work_order_linked()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Triaged, Guid.NewGuid());
        var service = CreateService(dbContext);

        var firstResponse = await service.ConvertAsync(
            defect.Id,
            new ConvertDefectToWorkOrderRequest { DueUtc = DueUtc },
            Guid.NewGuid(),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<DefectTransitionNotAllowedException>(
            () => service.ConvertAsync(
                defect.Id,
                new ConvertDefectToWorkOrderRequest { DueUtc = DueUtc.AddDays(1) },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(DefectStatus.Converted, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Converted, exception.RequestedStatus);

        var workOrders = await dbContext.Set<MaintenanceWorkOrder>().AsNoTracking().ToListAsync();
        var workOrder = Assert.Single(workOrders);
        Assert.Equal(firstResponse.WorkOrderId, workOrder.Id);

        var storedDefect = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();
        Assert.Equal(firstResponse.WorkOrderId, storedDefect.WorkOrderId);
    }

    [Fact]
    public async Task ConvertAsync_rejects_a_missing_due_utc_and_creates_no_work_order()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Triaged, Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<ConvertDefectToWorkOrderValidationException>(
            () => CreateService(dbContext).ConvertAsync(
                defect.Id,
                new ConvertDefectToWorkOrderRequest { DueUtc = null },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Contains(ConvertDefectToWorkOrderValidator.DueUtcField, exception.Errors);
        Assert.Empty(await dbContext.Set<MaintenanceWorkOrder>().ToListAsync());
    }

    [Fact]
    public async Task ConvertAsync_rejects_an_unknown_defect()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DefectNotFoundException>(
            () => CreateService(dbContext).ConvertAsync(
                missingId,
                new ConvertDefectToWorkOrderRequest { DueUtc = DueUtc },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(missingId, exception.DefectId);
    }

    private static async Task<VesselDefect> SeedAtStatusAsync(HarborOpsDbContext dbContext, DefectStatus status, Guid vesselId)
    {
        var defect = VesselDefect.Register(
            vesselId,
            null,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            ObservedAtUtc,
            Guid.NewGuid(),
            ReportedAtUtc);

        if (status is DefectStatus.Triaged or DefectStatus.Converted)
        {
            defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), UtcNow);
        }

        if (status is DefectStatus.Converted)
        {
            defect.ConvertToWorkOrder(Guid.NewGuid(), Guid.NewGuid(), UtcNow);
        }

        await dbContext.Set<VesselDefect>().AddAsync(defect);
        await dbContext.SaveChangesAsync();

        return defect;
    }

    private static ConvertDefectToWorkOrderService CreateService(HarborOpsDbContext dbContext)
    {
        return new ConvertDefectToWorkOrderService(dbContext, new FixedTimeProvider(FixedNow));
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
