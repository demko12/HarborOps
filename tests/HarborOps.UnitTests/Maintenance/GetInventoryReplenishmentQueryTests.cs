using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetInventoryReplenishmentQueryTests
{
    private static readonly DateTime UtcNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(UtcNow);

    [Fact]
    public async Task GetReplenishment_returns_only_items_below_the_reorder_level()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        await SeedItemAsync(dbContext, supplier.Id, "Low part", "PN-1", quantity: 1m, minimum: 5m);
        await SeedItemAsync(dbContext, supplier.Id, "Stocked part", "PN-2", quantity: 40m, minimum: 5m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal("Low part", row.Name);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task GetReplenishment_projects_stock_reservations_and_shortage()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 8m,
            minimum: 10m);

        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Open);
        await SeedReservationAsync(dbContext, workOrder.Id, item.Id, requested: 3m, available: 8m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal(item.Id, row.InventoryItemId);
        Assert.Equal("FILTER-001", row.PartNumber);
        Assert.Equal(supplier.Id, row.SupplierId);
        Assert.Equal("Marine Parts Ltd", row.SupplierName);
        Assert.Equal(8m, row.StockOnHand);
        Assert.Equal(3m, row.ReservedQuantity);
        Assert.Equal(5m, row.AvailableQuantity);
        Assert.Equal(10m, row.ReorderLevel);
        Assert.Equal(5m, row.ShortageQuantity);
        Assert.Equal(0m, row.OpenPurchaseOrderQuantity);
    }

    [Fact]
    public async Task Reservations_can_push_a_stocked_item_onto_the_board()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 10m,
            minimum: 6m);

        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.InProgress);
        await SeedReservationAsync(dbContext, workOrder.Id, item.Id, requested: 7m, available: 10m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal(3m, row.AvailableQuantity);
        Assert.Equal(3m, row.ShortageQuantity);
    }

    [Fact]
    public async Task Holds_from_finished_work_orders_are_not_counted()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 10m,
            minimum: 6m);

        var canceled = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.Canceled);
        await SeedReservationAsync(dbContext, canceled.Id, item.Id, requested: 7m, available: 10m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Consumed_reservations_no_longer_hold_stock()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 8m,
            minimum: 10m);

        var workOrder = await SeedWorkOrderAsync(dbContext, WorkOrderStatus.InProgress);
        var reservation = await SeedReservationAsync(
            dbContext,
            workOrder.Id,
            item.Id,
            requested: 4m,
            available: 8m);

        reservation.Consume(4m, UtcNow);
        await dbContext.SaveChangesAsync();

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal(0m, row.ReservedQuantity);
        Assert.Equal(8m, row.AvailableQuantity);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, 12)]
    [InlineData(PurchaseOrderStatus.Ordered, 12)]
    [InlineData(PurchaseOrderStatus.Received, 0)]
    [InlineData(PurchaseOrderStatus.Cancelled, 0)]
    public async Task Open_purchase_orders_count_only_draft_and_ordered_lines(
        PurchaseOrderStatus status,
        int expectedIncoming)
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 1m,
            minimum: 20m);

        await SeedPurchaseOrderAsync(dbContext, supplier.Id, item.Id, quantity: 12m, status);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal(expectedIncoming, row.OpenPurchaseOrderQuantity);
    }

    [Fact]
    public async Task Incoming_stock_sums_every_open_line_for_the_item()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 1m,
            minimum: 20m);

        await SeedPurchaseOrderAsync(dbContext, supplier.Id, item.Id, 5m, PurchaseOrderStatus.Draft);
        await SeedPurchaseOrderAsync(dbContext, supplier.Id, item.Id, 7m, PurchaseOrderStatus.Ordered);
        await SeedPurchaseOrderAsync(dbContext, supplier.Id, item.Id, 9m, PurchaseOrderStatus.Received);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Equal(12m, response.Items[0].OpenPurchaseOrderQuantity);
    }

    [Fact]
    public async Task An_item_without_available_stock_is_classified_as_critical()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        await SeedItemAsync(dbContext, supplier.Id, "Empty part", "PN-1", quantity: 0m, minimum: 5m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Equal(InventoryReplenishmentClassifications.Critical, response.Items[0].Classification);
    }

    [Fact]
    public async Task An_item_still_holding_stock_is_classified_as_low()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        await SeedItemAsync(dbContext, supplier.Id, "Low part", "PN-1", quantity: 3m, minimum: 5m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Equal(InventoryReplenishmentClassifications.Low, response.Items[0].Classification);
    }

    [Fact]
    public async Task An_item_whose_shortage_is_on_order_is_classified_as_covered()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        var item = await SeedItemAsync(
            dbContext,
            supplier.Id,
            "Low part",
            "PN-1",
            quantity: 3m,
            minimum: 5m);

        await SeedPurchaseOrderAsync(dbContext, supplier.Id, item.Id, 10m, PurchaseOrderStatus.Ordered);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Equal(InventoryReplenishmentClassifications.Covered, response.Items[0].Classification);
    }

    [Fact]
    public async Task GetReplenishment_orders_by_shortage_then_name_then_part_number()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");
        await SeedItemAsync(dbContext, supplier.Id, "Small gap", "PN-3", quantity: 4m, minimum: 5m);
        await SeedItemAsync(dbContext, supplier.Id, "Huge gap", "PN-1", quantity: 0m, minimum: 20m);
        await SeedItemAsync(dbContext, supplier.Id, "Tied gap", "PN-2", quantity: 4m, minimum: 5m);

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Equal(
            ["Huge gap", "Small gap", "Tied gap"],
            response.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task GetReplenishment_returns_the_first_page_by_default()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");

        for (var index = 1; index <= 26; index++)
        {
            await SeedItemAsync(
                dbContext,
                supplier.Id,
                $"Part {index:D2}",
                $"PN-{index:D2}",
                quantity: 0m,
                minimum: 1m);
        }

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(26, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Part 01", response.Items[0].Name);
        Assert.Equal("Part 25", response.Items[^1].Name);
    }

    [Fact]
    public async Task GetReplenishment_applies_custom_paging()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Marine Parts Ltd");

        for (var index = 1; index <= 5; index++)
        {
            await SeedItemAsync(
                dbContext,
                supplier.Id,
                $"Part {index:D2}",
                $"PN-{index:D2}",
                quantity: 0m,
                minimum: 1m);
        }

        var response = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(["Part 03", "Part 04"], response.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task GetReplenishment_rejects_a_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetInventoryReplenishmentValidationException>(
            () => new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
                new GetInventoryReplenishmentRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(GetInventoryReplenishmentValidator.PageNumberField, exception.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GetInventoryReplenishmentValidator.MaxPageSize + 1)]
    public async Task GetReplenishment_rejects_an_out_of_range_page_size(int pageSize)
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetInventoryReplenishmentValidationException>(
            () => new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
                new GetInventoryReplenishmentRequest { PageSize = pageSize },
                CancellationToken.None));

        Assert.Contains(GetInventoryReplenishmentValidator.PageSizeField, exception.Errors);
    }

    private static async Task<Supplier> SeedSupplierAsync(HarborOpsDbContext dbContext, string name)
    {
        var supplier = Supplier.Register(name, null, null, null, null);

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync();

        return supplier;
    }

    private static async Task<InventoryItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        Guid supplierId,
        string name,
        string partNumber,
        decimal quantity,
        decimal minimum)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            supplierId,
            name,
            partNumber,
            quantity,
            minimum,
            unitCost: 150m);

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
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

    private static async Task<WorkOrderPartReservation> SeedReservationAsync(
        HarborOpsDbContext dbContext,
        Guid workOrderId,
        Guid inventoryItemId,
        decimal requested,
        decimal available)
    {
        var reservation = WorkOrderPartReservation.Create(
            workOrderId,
            inventoryItemId,
            requested,
            available,
            UtcNow);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();

        return reservation;
    }

    private static async Task SeedPurchaseOrderAsync(
        HarborOpsDbContext dbContext,
        Guid supplierId,
        Guid inventoryItemId,
        decimal quantity,
        PurchaseOrderStatus status)
    {
        var purchaseOrder = PurchaseOrder.Create(supplierId, Today, Today.AddDays(14));

        switch (status)
        {
            case PurchaseOrderStatus.Ordered:
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                break;

            case PurchaseOrderStatus.Received:
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Received);
                break;

            case PurchaseOrderStatus.Cancelled:
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Cancelled);
                break;

            default:
                break;
        }

        var line = PurchaseOrderLine.Create(purchaseOrder.Id, inventoryItemId, quantity, 100m);

        await dbContext.Set<PurchaseOrder>().AddAsync(purchaseOrder);
        await dbContext.Set<PurchaseOrderLine>().AddAsync(line);
        await dbContext.SaveChangesAsync();
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
