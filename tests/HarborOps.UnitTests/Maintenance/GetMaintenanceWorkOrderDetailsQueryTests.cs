using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetMaintenanceWorkOrderDetailsQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task GetDetailsAsync_returns_every_scalar_field_of_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var engineerId = Guid.NewGuid();
        var workOrder = MaintenanceWorkOrder.Create(
            vesselId,
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            UtcNow.AddDays(5),
            dryDockId: null,
            assignedEngineerId: engineerId);
        await SeedAsync(dbContext, workOrder);

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(workOrder.Id, details.WorkOrderId);
        Assert.Equal(vesselId, details.VesselId);
        Assert.Null(details.DryDockId);
        Assert.Equal(engineerId, details.AssignedEngineerId);
        Assert.Equal("Replace main engine filter", details.Title);
        Assert.Equal("Filter is due for replacement", details.Description);
        Assert.Equal("High", details.Priority);
        Assert.Equal("Open", details.Status);
        Assert.Equal(UtcNow.AddDays(5), details.DueUtc);
        Assert.Equal(workOrder.CreatedUtc, details.CreatedUtc);
        Assert.Null(details.CompletedUtc);
        Assert.False(details.IsOverdue);
    }

    [Fact]
    public async Task An_open_work_order_without_scope_returns_empty_collections()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateWorkOrder(Guid.NewGuid());
        await SeedAsync(dbContext, workOrder);

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Empty(details.Equipment);
        Assert.Empty(details.PartUsages);
        Assert.Null(details.Completion);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_the_linked_equipment_ordered_by_name()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = CreateWorkOrder(vesselId);
        var boiler = Equipment.Register(vesselId, "Boiler", "Steam", "Alfa", "SN-2");
        var mainEngine = Equipment.Register(vesselId, "Main engine", "Propulsion", "MAN", "SN-1");
        var unlinked = Equipment.Register(vesselId, "Auxiliary pump", "Pumps", null, null);

        await SeedAsync(dbContext, workOrder);
        await dbContext.Set<Equipment>().AddRangeAsync(boiler, mainEngine, unlinked);
        await dbContext.Set<WorkOrderEquipment>().AddRangeAsync(
            WorkOrderEquipment.Create(workOrder.Id, mainEngine.Id),
            WorkOrderEquipment.Create(workOrder.Id, boiler.Id));
        await dbContext.SaveChangesAsync();

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(2, details.Equipment.Count);
        Assert.Equal(boiler.Id, details.Equipment[0].EquipmentId);
        Assert.Equal("Boiler", details.Equipment[0].Name);
        Assert.Equal("Steam", details.Equipment[0].Category);
        Assert.Equal("Alfa", details.Equipment[0].Manufacturer);
        Assert.Equal("SN-2", details.Equipment[0].SerialNumber);
        Assert.Equal(mainEngine.Id, details.Equipment[1].EquipmentId);
        Assert.DoesNotContain(details.Equipment, item => item.EquipmentId == unlinked.Id);
    }

    [Fact]
    public async Task GetDetailsAsync_ignores_equipment_linked_to_another_work_order()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = CreateWorkOrder(vesselId);
        var otherWorkOrder = CreateWorkOrder(vesselId);
        var mine = Equipment.Register(vesselId, "Main engine", "Propulsion", null, null);
        var theirs = Equipment.Register(vesselId, "Bow thruster", "Propulsion", null, null);

        await SeedAsync(dbContext, workOrder, otherWorkOrder);
        await dbContext.Set<Equipment>().AddRangeAsync(mine, theirs);
        await dbContext.Set<WorkOrderEquipment>().AddRangeAsync(
            WorkOrderEquipment.Create(workOrder.Id, mine.Id),
            WorkOrderEquipment.Create(otherWorkOrder.Id, theirs.Id));
        await dbContext.SaveChangesAsync();

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        var row = Assert.Single(details.Equipment);
        Assert.Equal(mine.Id, row.EquipmentId);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_part_usage_with_the_remaining_quantity()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateWorkOrder(Guid.NewGuid());
        var filter = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
            "FILTER-001",
            7.5m,
            2m,
            150m);

        await SeedAsync(dbContext, workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(filter);
        await dbContext.Set<InventoryItemUsage>().AddAsync(
            InventoryItemUsage.Create(workOrder.Id, filter.Id, 2.5m));
        await dbContext.SaveChangesAsync();

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        var usage = Assert.Single(details.PartUsages);
        Assert.Equal(filter.Id, usage.PartId);
        Assert.Equal("Main engine filter", usage.Name);
        Assert.Equal("FILTER-001", usage.PartNumber);
        Assert.Equal(2.5m, usage.QuantityUsed);
        Assert.Equal(7.5m, usage.RemainingQuantity);
    }

    [Fact]
    public async Task A_completed_work_order_returns_its_completion_record()
    {
        await using var dbContext = CreateDbContext();
        var technicianId = Guid.NewGuid();
        var completedUtc = UtcNow.AddDays(-2);
        var workOrder = CreateWorkOrder(Guid.NewGuid());
        workOrder.Start();
        workOrder.Complete(completedUtc);
        var record = MaintenanceRecord.Create(
            workOrder.Id,
            technicianId,
            DateOnly.FromDateTime(completedUtc),
            "Filter replaced and tested",
            1_250.50m,
            6.5m);

        await SeedAsync(dbContext, workOrder);
        await dbContext.Set<MaintenanceRecord>().AddAsync(record);
        await dbContext.SaveChangesAsync();

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal("Completed", details.Status);
        Assert.Equal(completedUtc, details.CompletedUtc);
        Assert.NotNull(details.Completion);
        Assert.Equal(record.Id, details.Completion!.MaintenanceRecordId);
        Assert.Equal(technicianId, details.Completion.TechnicianId);
        Assert.Equal(DateOnly.FromDateTime(completedUtc), details.Completion.CompletionDate);
        Assert.Equal("Filter replaced and tested", details.Completion.Notes);
        Assert.Equal(1_250.50m, details.Completion.Cost);
        Assert.Equal(6.5m, details.Completion.LaborHours);
    }

    [Fact]
    public async Task GetDetailsAsync_ignores_a_completion_record_of_another_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateWorkOrder(Guid.NewGuid());
        var otherWorkOrder = CreateWorkOrder(Guid.NewGuid());

        await SeedAsync(dbContext, workOrder, otherWorkOrder);
        await dbContext.Set<MaintenanceRecord>().AddAsync(
            MaintenanceRecord.Create(
                otherWorkOrder.Id,
                Guid.NewGuid(),
                DateOnly.FromDateTime(UtcNow),
                null,
                null,
                null));
        await dbContext.SaveChangesAsync();

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Null(details.Completion);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Open, new[] { "Start", "Cancel", "ReplaceEquipment" })]
    [InlineData(WorkOrderStatus.InProgress, new[] { "Cancel", "ReplaceEquipment" })]
    [InlineData(WorkOrderStatus.Completed, new string[0])]
    [InlineData(WorkOrderStatus.Canceled, new string[0])]
    public async Task GetDetailsAsync_reports_the_actions_allowed_for_the_current_status(
        WorkOrderStatus status,
        string[] expected)
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateWorkOrder(Guid.NewGuid());

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

        await SeedAsync(dbContext, workOrder);

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Equal(status.ToString(), details.Status);
        Assert.Equal(expected, details.AllowedActions);
    }

    [Fact]
    public async Task An_open_work_order_past_its_due_date_is_reported_overdue()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Late inspection",
            null,
            WorkOrderPriority.Medium,
            UtcNow.AddDays(-3));
        await SeedAsync(dbContext, workOrder);

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.True(details.IsOverdue);
    }

    [Fact]
    public async Task A_cancelled_work_order_past_its_due_date_is_not_overdue()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Abandoned inspection",
            null,
            WorkOrderPriority.Medium,
            UtcNow.AddDays(-3));
        workOrder.Cancel();
        await SeedAsync(dbContext, workOrder);

        var details = await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.False(details.IsOverdue);
    }

    [Fact]
    public async Task GetDetailsAsync_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateQuery(dbContext).GetDetailsAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
    }

    [Fact]
    public async Task GetDetailsAsync_does_not_track_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateWorkOrder(Guid.NewGuid());
        await SeedAsync(dbContext, workOrder);
        dbContext.ChangeTracker.Clear();

        await CreateQuery(dbContext).GetDetailsAsync(workOrder.Id, CancellationToken.None);

        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static MaintenanceWorkOrder CreateWorkOrder(Guid vesselId)
    {
        return MaintenanceWorkOrder.Create(
            vesselId,
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            UtcNow.AddDays(7));
    }

    private static async Task SeedAsync(
        HarborOpsDbContext dbContext,
        params MaintenanceWorkOrder[] workOrders)
    {
        await dbContext.Set<MaintenanceWorkOrder>().AddRangeAsync(workOrders);
        await dbContext.SaveChangesAsync();
    }

    private static GetMaintenanceWorkOrderDetailsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetMaintenanceWorkOrderDetailsQuery(dbContext, new FixedTimeProvider(FixedNow));
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
