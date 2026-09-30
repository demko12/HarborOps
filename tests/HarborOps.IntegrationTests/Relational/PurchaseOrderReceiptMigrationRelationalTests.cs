using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Upgrades a database that already holds purchase orders and supplier returns, so the receipt migration
/// is proven to give legacy received orders an explicit opening balance without touching inventory.
/// </summary>
public sealed class PurchaseOrderReceiptMigrationRelationalTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260928074656_AddFleetPositionReportHistory";

    private static readonly Guid ActorUserId = Guid.Parse("3c4d5e6f-7081-4923-a4b5-c6d7e8f90a1b");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    private readonly Guid itemId = Guid.NewGuid();
    private readonly Guid receivedOrderId = Guid.NewGuid();
    private readonly Guid receivedLineId = Guid.NewGuid();
    private readonly Guid receivedSecondLineId = Guid.NewGuid();
    private readonly Guid orderedOrderId = Guid.NewGuid();
    private readonly Guid orderedLineId = Guid.NewGuid();
    private readonly Guid cancelledOrderId = Guid.NewGuid();
    private readonly Guid cancelledLineId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await SeedLegacyDataAsync(dbContext);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task Upgrading_records_a_legacy_opening_balance_only_for_received_orders()
    {
        await using var dbContext = CreateDbContext();
        var lines = await dbContext.Set<PurchaseOrderLine>().AsNoTracking().ToDictionaryAsync(line => line.Id);
        var orders = await dbContext.Set<PurchaseOrder>().AsNoTracking().ToListAsync();

        Assert.Equal(10m, lines[receivedLineId].ReceivedQuantity);
        Assert.Equal(10m, lines[receivedLineId].LegacyReceivedQuantity);
        Assert.Equal(2.5m, lines[receivedSecondLineId].ReceivedQuantity);
        Assert.Equal(2.5m, lines[receivedSecondLineId].LegacyReceivedQuantity);
        Assert.Equal(0m, lines[orderedLineId].ReceivedQuantity);
        Assert.Equal(0m, lines[orderedLineId].LegacyReceivedQuantity);
        Assert.Equal(0m, lines[cancelledLineId].ReceivedQuantity);

        // No receipt, date, or actor is invented, and inventory is untouched.
        Assert.Empty(await dbContext.Set<PurchaseOrderReceipt>().AsNoTracking().ToListAsync());
        Assert.Equal(7m, (await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync()).Quantity);

        Assert.Equal(PurchaseOrderStatus.Received, orders.Single(order => order.Id == receivedOrderId).Status);
        Assert.All(orders, order => Assert.NotEqual(Guid.Empty, order.ConcurrencyToken));
        Assert.Equal(orders.Count, orders.Select(order => order.ConcurrencyToken).Distinct().Count());
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    [RelationalFact]
    public async Task A_legacy_received_order_reports_as_fully_received_with_existing_returns()
    {
        await using var dbContext = CreateDbContext();

        var details = await new GetPurchaseOrderDetailsQuery(dbContext, TimeProvider.System)
            .GetDetailsAsync(receivedOrderId, CancellationToken.None);

        var line = details.Lines.Single(candidate => candidate.LineId == receivedLineId);

        Assert.Equal(nameof(PurchaseOrderStatus.Received), details.Status);
        Assert.Equal(0, details.ReceiptCount);
        Assert.Equal(0m, details.TotalQuantityOutstanding);
        Assert.Equal(10m, line.LegacyReceivedQuantity);
        Assert.Equal(4m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ReturnableQuantity);
        Assert.Equal([PurchaseOrderActions.CreateReturn], details.AllowedActions);
    }

    [RelationalFact]
    public async Task A_legacy_received_order_cannot_be_received_again()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<PurchaseOrderReceiptPostingBlockedException>(
            () => new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
                .ReceiveRemainingAsync(receivedOrderId, null, ActorUserId, CancellationToken.None));

        await using var verification = CreateDbContext();
        Assert.Equal(7m, (await verification.Set<InventoryItem>().AsNoTracking().SingleAsync()).Quantity);
    }

    [RelationalFact]
    public async Task Legacy_returns_count_against_the_opening_balance()
    {
        await using var dbContext = CreateDbContext();
        var service = new CreateSupplierReturnService(dbContext, TimeProvider.System);

        var withinBalance = await service.CreateAsync(ReturnRequest(6m), ActorUserId, CancellationToken.None);
        var aboveBalance = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(
            () => service.CreateAsync(ReturnRequest(6.001m), ActorUserId, CancellationToken.None));

        Assert.Equal(6m, Assert.Single(withinBalance.Lines).RemainingReturnableQuantity);
        Assert.Equal(["lines[0].quantity"], aboveBalance.Errors.Keys);
    }

    [RelationalFact]
    public async Task An_order_that_was_still_open_can_now_be_received_in_deliveries()
    {
        var receivedUtc = DateTime.UtcNow.AddHours(-1);
        PurchaseOrderReceiptLineEntry[] lines =
            [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = orderedLineId, Quantity = 1m }];

        await using (var dbContext = CreateDbContext())
        {
            var preview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, TimeProvider.System).GetPreviewAsync(
                orderedOrderId,
                new PreviewPurchaseOrderReceiptRequest { ReceivedUtc = receivedUtc, Lines = lines },
                CancellationToken.None);

            var response = await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System).PostAsync(
                orderedOrderId,
                new PostPurchaseOrderReceiptRequest
                {
                    ClientRequestId = Guid.NewGuid(),
                    ExpectedConcurrencyToken = preview.ConcurrencyToken,
                    PreviewToken = preview.PreviewToken,
                    ReceivedUtc = receivedUtc,
                    Lines = lines,
                },
                ActorUserId,
                CancellationToken.None);

            Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.OrderStatus);
            Assert.Equal(7m, Assert.Single(response.Receipt.Movements).QuantityBefore);
        }

        await using var verification = CreateDbContext();
        Assert.Equal(8m, (await verification.Set<InventoryItem>().AsNoTracking().SingleAsync()).Quantity);
    }

    [RelationalFact]
    public async Task Rolling_back_drops_the_receipt_schema()
    {
        const int InvalidObjectName = 208;
        const int InvalidColumnName = 207;

        await using (var dbContext = CreateDbContext())
        {
            await PostRemainingAsync(dbContext, orderedOrderId);
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using var rolledBack = CreateDbContext();

        var missingTable = await Assert.ThrowsAsync<SqlException>(
            () => rolledBack.Set<PurchaseOrderReceipt>().AnyAsync());
        var missingColumns = await Assert.ThrowsAsync<SqlException>(
            () => rolledBack.Set<PurchaseOrderLine>().Select(line => line.ReceivedQuantity).ToListAsync());

        Assert.Equal(InvalidObjectName, missingTable.Number);
        Assert.Equal(InvalidColumnName, missingColumns.Number);
        Assert.Equal(PreviousMigration, (await rolledBack.Database.GetAppliedMigrationsAsync()).Last());
    }

    private static Task<PostPurchaseOrderReceiptResponse> PostRemainingAsync(HarborOpsDbContext dbContext, Guid purchaseOrderId)
    {
        return new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .ReceiveRemainingAsync(purchaseOrderId, null, ActorUserId, CancellationToken.None);
    }

    private CreateSupplierReturnRequest ReturnRequest(decimal quantity)
    {
        return new CreateSupplierReturnRequest
        {
            PurchaseOrderId = receivedOrderId,
            Lines =
            [
                new SupplierReturnLineEntry
                {
                    PurchaseOrderLineId = receivedLineId,
                    Quantity = quantity,
                    Reason = "Wrong part",
                },
            ],
        };
    }

    // Written as raw SQL against the previous schema, exactly as rows created before receipts existed.
    private async Task SeedLegacyDataAsync(HarborOpsDbContext dbContext)
    {
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var supplierReturnId = Guid.NewGuid();
        var createdUtc = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO [maintenance].[ItemCategories] ([Id], [CategoryName]) VALUES ({categoryId}, N'Spares');
            INSERT INTO [maintenance].[UnitsOfMeasure] ([Id], [UnitName], [Abbreviation]) VALUES ({unitId}, N'Piece', N'pcs');
            INSERT INTO [maintenance].[Suppliers] ([Id], [Name]) VALUES ({supplierId}, N'Legacy supplier');
            INSERT INTO [maintenance].[InventoryItems]
                ([Id], [CategoryId], [UnitId], [SupplierId], [Name], [PartNumber], [Quantity], [MinimumQuantity], [UnitCost])
            VALUES ({itemId}, {categoryId}, {unitId}, {supplierId}, N'Main engine filter', N'PN-LEGACY', 7, 1, 25);
            INSERT INTO [maintenance].[PurchaseOrders] ([Id], [SupplierId], [OrderDate], [Status])
            VALUES ({receivedOrderId}, {supplierId}, '2026-08-01', N'Received'),
                   ({orderedOrderId}, {supplierId}, '2026-08-02', N'Ordered'),
                   ({cancelledOrderId}, {supplierId}, '2026-08-03', N'Cancelled');
            INSERT INTO [maintenance].[PurchaseOrderLines] ([Id], [PurchaseOrderId], [PartId], [QuantityOrdered], [UnitCost])
            VALUES ({receivedLineId}, {receivedOrderId}, {itemId}, 10, 25),
                   ({receivedSecondLineId}, {receivedOrderId}, {itemId}, 2.5, 25),
                   ({orderedLineId}, {orderedOrderId}, {itemId}, 4, 25),
                   ({cancelledLineId}, {cancelledOrderId}, {itemId}, 3, 25);
            INSERT INTO [maintenance].[SupplierReturns]
                ([Id], [PurchaseOrderId], [SupplierId], [Status], [CreatedByUserId], [CreatedUtc], [PostedByUserId], [PostedUtc])
            VALUES ({supplierReturnId}, {receivedOrderId}, {supplierId}, N'Posted', {ActorUserId}, {createdUtc}, {ActorUserId}, {createdUtc});
            INSERT INTO [maintenance].[SupplierReturnLines]
                ([Id], [SupplierReturnId], [PurchaseOrderLineId], [InventoryItemId], [Quantity], [Reason])
            VALUES ({Guid.NewGuid()}, {supplierReturnId}, {receivedLineId}, {itemId}, 4, N'Damaged');
            """);
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
