using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ReplaceWorkOrderEquipmentServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task ReplaceEquipmentAsync_links_every_requested_equipment()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");

        var response = await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [mainEngine.Id, boiler.Id] },
            CancellationToken.None);

        var links = await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync();

        Assert.Equal(workOrder.Id, response.WorkOrderId);
        Assert.Equal("Open", response.Status);
        Assert.Equal(["Start", "Cancel", "ReplaceEquipment"], response.AllowedActions);
        Assert.Equal(UtcNow, response.UpdatedUtc);
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(workOrder.Id, link.WorkOrderId));
        Assert.Contains(links, link => link.EquipmentId == mainEngine.Id);
        Assert.Contains(links, link => link.EquipmentId == boiler.Id);
    }

    [Fact]
    public async Task The_response_returns_the_new_scope_ordered_by_name()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var mainEngine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");

        var response = await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [mainEngine.Id, boiler.Id] },
            CancellationToken.None);

        Assert.Equal([boiler.Id, mainEngine.Id], response.Equipment.Select(item => item.EquipmentId));
        Assert.Equal("Boiler", response.Equipment[0].Name);
        Assert.Equal(vesselId, response.Equipment[0].VesselId);
    }

    [Fact]
    public async Task ReplaceEquipmentAsync_replaces_the_previous_scope()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var removed = await SeedEquipmentAsync(dbContext, vesselId, "Bow thruster");
        var added = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        await SeedLinksAsync(dbContext, workOrder.Id, removed.Id);

        await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [added.Id] },
            CancellationToken.None);

        var links = await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync();

        var link = Assert.Single(links);
        Assert.Equal(added.Id, link.EquipmentId);
    }

    [Fact]
    public async Task Keeping_an_already_linked_equipment_does_not_duplicate_the_composite_key()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var kept = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var added = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        var removed = await SeedEquipmentAsync(dbContext, vesselId, "Bow thruster");
        await SeedLinksAsync(dbContext, workOrder.Id, kept.Id, removed.Id);

        await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [kept.Id, added.Id] },
            CancellationToken.None);

        var links = await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync();

        Assert.Equal(2, links.Count);
        Assert.Equal(2, links.Select(link => link.EquipmentId).Distinct().Count());
        Assert.Contains(links, link => link.EquipmentId == kept.Id);
        Assert.Contains(links, link => link.EquipmentId == added.Id);
        Assert.DoesNotContain(links, link => link.EquipmentId == removed.Id);
    }

    [Fact]
    public async Task An_empty_selection_clears_the_scope()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        await SeedLinksAsync(dbContext, workOrder.Id, equipment.Id);

        var response = await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [] },
            CancellationToken.None);

        Assert.Empty(response.Equipment);
        Assert.Empty(await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReplaceEquipmentAsync_leaves_other_work_orders_untouched()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var otherWorkOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var shared = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var mine = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        await SeedLinksAsync(dbContext, otherWorkOrder.Id, shared.Id);

        await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [mine.Id] },
            CancellationToken.None);

        var otherLinks = await dbContext.Set<WorkOrderEquipment>()
            .AsNoTracking()
            .Where(link => link.WorkOrderId == otherWorkOrder.Id)
            .ToListAsync();

        var otherLink = Assert.Single(otherLinks);
        Assert.Equal(shared.Id, otherLink.EquipmentId);
    }

    [Fact]
    public async Task A_null_selection_is_rejected()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderEquipmentValidationException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest(),
                CancellationToken.None));

        Assert.Contains(ReplaceWorkOrderEquipmentValidator.EquipmentIdsField, exception.Errors);
    }

    [Fact]
    public async Task A_duplicated_equipment_selection_is_rejected()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderEquipmentValidationException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [equipment.Id, equipment.Id] },
                CancellationToken.None));

        Assert.Contains(ReplaceWorkOrderEquipmentValidator.EquipmentIdsField, exception.Errors);
    }

    [Fact]
    public async Task An_empty_guid_in_the_selection_is_rejected()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = await SeedWorkOrderAsync(dbContext, Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<ReplaceWorkOrderEquipmentValidationException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [Guid.Empty] },
                CancellationToken.None));

        Assert.Contains(ReplaceWorkOrderEquipmentValidator.EquipmentIdsField, exception.Errors);
    }

    [Fact]
    public async Task ReplaceEquipmentAsync_rejects_an_unknown_work_order()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<MaintenanceWorkOrderNotFoundException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                missingId,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [Guid.NewGuid()] },
                CancellationToken.None));

        Assert.Equal(missingId, exception.WorkOrderId);
    }

    [Fact]
    public async Task ReplaceEquipmentAsync_rejects_an_unknown_equipment_row()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var known = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<WorkOrderEquipmentNotFoundException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [known.Id, missingId] },
                CancellationToken.None));

        Assert.Equal([missingId], exception.EquipmentIds);
    }

    [Fact]
    public async Task ReplaceEquipmentAsync_rejects_equipment_of_another_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var mine = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var foreign = await SeedEquipmentAsync(dbContext, otherVesselId, "Foreign boiler");

        var exception = await Assert.ThrowsAsync<WorkOrderEquipmentVesselMismatchException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [mine.Id, foreign.Id] },
                CancellationToken.None));

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(vesselId, exception.VesselId);
        Assert.Equal([foreign.Id], exception.EquipmentIds);
    }

    [Fact]
    public async Task A_failed_replacement_leaves_the_previous_scope_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var first = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var second = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        var foreign = await SeedEquipmentAsync(dbContext, Guid.NewGuid(), "Foreign pump");
        await SeedLinksAsync(dbContext, workOrder.Id, first.Id, second.Id);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorkOrderEquipmentVesselMismatchException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [foreign.Id] },
                CancellationToken.None));

        var links = await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync();

        Assert.Equal(2, links.Count);
        Assert.Contains(links, link => link.EquipmentId == first.Id);
        Assert.Contains(links, link => link.EquipmentId == second.Id);
    }

    [Fact]
    public async Task A_replacement_with_an_unknown_row_leaves_the_previous_scope_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId);
        var existing = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        await SeedLinksAsync(dbContext, workOrder.Id, existing.Id);
        dbContext.ChangeTracker.Clear();

        await Assert.ThrowsAsync<WorkOrderEquipmentNotFoundException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [Guid.NewGuid()] },
                CancellationToken.None));

        var link = Assert.Single(await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync());
        Assert.Equal(existing.Id, link.EquipmentId);
    }

    [Fact]
    public async Task A_work_order_in_progress_still_accepts_an_equipment_change()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId, WorkOrderStatus.InProgress);
        var equipment = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");

        var response = await CreateService(dbContext).ReplaceEquipmentAsync(
            workOrder.Id,
            new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [equipment.Id] },
            CancellationToken.None);

        Assert.Equal("InProgress", response.Status);
        Assert.Equal(["Cancel", "ReplaceEquipment"], response.AllowedActions);
        Assert.Single(await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task A_closed_work_order_refuses_an_equipment_change(WorkOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(dbContext, vesselId, status);
        var existing = await SeedEquipmentAsync(dbContext, vesselId, "Main engine");
        var wanted = await SeedEquipmentAsync(dbContext, vesselId, "Boiler");
        await SeedLinksAsync(dbContext, workOrder.Id, existing.Id);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorkOrderEquipmentChangeNotAllowedException>(
            () => CreateService(dbContext).ReplaceEquipmentAsync(
                workOrder.Id,
                new ReplaceWorkOrderEquipmentRequest { EquipmentIds = [wanted.Id] },
                CancellationToken.None));

        Assert.Equal(status, exception.CurrentStatus);

        var link = Assert.Single(await dbContext.Set<WorkOrderEquipment>().AsNoTracking().ToListAsync());
        Assert.Equal(existing.Id, link.EquipmentId);
    }

    private static async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        WorkOrderStatus status = WorkOrderStatus.Open)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            vesselId,
            "Replace main engine filter",
            "Filter is due for replacement",
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

    private static async Task<Equipment> SeedEquipmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        string name)
    {
        var equipment = Equipment.Register(vesselId, name, "Propulsion", "MAN", null);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.SaveChangesAsync();

        return equipment;
    }

    private static async Task SeedLinksAsync(
        HarborOpsDbContext dbContext,
        Guid workOrderId,
        params Guid[] equipmentIds)
    {
        foreach (var equipmentId in equipmentIds)
        {
            await dbContext.Set<WorkOrderEquipment>()
                .AddAsync(WorkOrderEquipment.Create(workOrderId, equipmentId));
        }

        await dbContext.SaveChangesAsync();
    }

    private static ReplaceWorkOrderEquipmentService CreateService(HarborOpsDbContext dbContext)
    {
        return new ReplaceWorkOrderEquipmentService(dbContext, new FixedTimeProvider(FixedNow));
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
