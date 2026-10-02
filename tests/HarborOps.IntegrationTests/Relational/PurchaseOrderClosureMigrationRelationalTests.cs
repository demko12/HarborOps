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
/// Upgrades a database that already holds orders in every status, a posted receipt, and a legacy balance,
/// so the closure migration is proven to keep persisted statuses and receipts and to close nothing by itself.
/// </summary>
public sealed class PurchaseOrderClosureMigrationRelationalTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260928124937_AddMaintenancePurchaseOrderReceipts";

    private static readonly Guid ActorUserId = Guid.Parse("7f8091a2-b3c4-4d5e-8f60-718293a4b5c6");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    private readonly Guid itemId = Guid.NewGuid();
    private readonly Guid receivedOrderId = Guid.NewGuid();
    private readonly Guid receivedLineId = Guid.NewGuid();
    private readonly Guid partialOrderId = Guid.NewGuid();
    private readonly Guid partialLineId = Guid.NewGuid();
    private readonly Guid partialConcurrencyToken = Guid.NewGuid();
    private readonly Guid receiptId = Guid.NewGuid();
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
        await SeedExistingDataAsync(dbContext);
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
    public async Task Upgrading_keeps_statuses_quantities_and_receipts_and_closes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var orders = await dbContext.Set<PurchaseOrder>().AsNoTracking().ToDictionaryAsync(order => order.Id);
        var lines = await dbContext.Set<PurchaseOrderLine>().AsNoTracking().ToDictionaryAsync(line => line.Id);

        Assert.Equal(PurchaseOrderStatus.Received, orders[receivedOrderId].Status);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, orders[partialOrderId].Status);
        Assert.Equal(PurchaseOrderStatus.Ordered, orders[orderedOrderId].Status);
        Assert.Equal(PurchaseOrderStatus.Cancelled, orders[cancelledOrderId].Status);
        Assert.Equal(partialConcurrencyToken, orders[partialOrderId].ConcurrencyToken);
        Assert.All(lines.Values, line => Assert.Equal(0m, line.ClosedQuantity));
        Assert.Equal(10m, lines[receivedLineId].LegacyReceivedQuantity);
        Assert.Equal(4m, lines[partialLineId].ReceivedQuantity);
        Assert.Equal(6m, lines[partialLineId].OutstandingQuantity);
        Assert.Equal(4m, lines[orderedLineId].OutstandingQuantity);

        var receipt = await dbContext.Set<PurchaseOrderReceipt>().AsNoTracking().SingleAsync();

        Assert.Equal(receiptId, receipt.Id);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, receipt.OrderStatusAfter);
        Assert.Equal(4m, (await dbContext.Set<PurchaseOrderReceiptLine>().AsNoTracking().SingleAsync()).Quantity);
        Assert.Empty(await dbContext.Set<PurchaseOrderClosure>().AsNoTracking().ToListAsync());
        Assert.Equal(7m, (await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync()).Quantity);
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    [RelationalFact]
    public async Task An_existing_partially_received_order_can_be_closed_short_after_upgrading()
    {
        await using (var dbContext = CreateDbContext())
        {
            var preview = await new GetPurchaseOrderClosurePreviewQuery(dbContext, TimeProvider.System)
                .GetPreviewAsync(partialOrderId, CancellationToken.None);

            Assert.True(preview.CanClose);
            Assert.Equal(6m, preview.TotalQuantityToClose);

            var response = await new ClosePurchaseOrderService(dbContext, TimeProvider.System).CloseAsync(
                partialOrderId,
                new ClosePurchaseOrderRequest
                {
                    ClientRequestId = Guid.NewGuid(),
                    ExpectedConcurrencyToken = preview.ConcurrencyToken,
                    PreviewToken = preview.PreviewToken,
                    Reason = "Supplier discontinued the part",
                },
                ActorUserId,
                CancellationToken.None);

            Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), response.OrderStatus);
            Assert.Equal(6m, response.Closure.TotalQuantityClosed);
        }

        await using var verification = CreateDbContext();
        Assert.Equal(7m, (await verification.Set<InventoryItem>().AsNoTracking().SingleAsync()).Quantity);
        Assert.Single(await verification.Set<PurchaseOrderReceipt>().AsNoTracking().ToListAsync());
    }

    [RelationalFact]
    public async Task Legacy_and_open_orders_cannot_be_closed_short_after_upgrading()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetPurchaseOrderClosurePreviewQuery(dbContext, TimeProvider.System);

        var legacy = await query.GetPreviewAsync(receivedOrderId, CancellationToken.None);
        var ordered = await query.GetPreviewAsync(orderedOrderId, CancellationToken.None);
        var cancelled = await query.GetPreviewAsync(cancelledOrderId, CancellationToken.None);

        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(legacy.Blockers).Code);
        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(ordered.Blockers).Code);
        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(cancelled.Blockers).Code);
    }

    [RelationalFact]
    public async Task Rolling_back_drops_the_closure_schema_and_keeps_receipts()
    {
        const int InvalidObjectName = 208;
        const int InvalidColumnName = 207;

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using var rolledBack = CreateDbContext();

        var missingTable = await Assert.ThrowsAsync<SqlException>(
            () => rolledBack.Set<PurchaseOrderClosure>().AnyAsync());
        var missingColumn = await Assert.ThrowsAsync<SqlException>(
            () => rolledBack.Set<PurchaseOrderLine>().Select(line => line.ClosedQuantity).ToListAsync());

        Assert.Equal(InvalidObjectName, missingTable.Number);
        Assert.Equal(InvalidColumnName, missingColumn.Number);
        Assert.Equal(1, await rolledBack.Set<PurchaseOrderReceipt>().CountAsync());
        Assert.Equal(PreviousMigration, (await rolledBack.Database.GetAppliedMigrationsAsync()).Last());
    }

    // Written as raw SQL against the previous schema, exactly as rows created before closures existed.
    private async Task SeedExistingDataAsync(HarborOpsDbContext dbContext)
    {
        var categoryId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var receivedUtc = new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
        var requestHash = new string('A', PurchaseOrderReceipt.RequestHashLength);

        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO [maintenance].[ItemCategories] ([Id], [CategoryName]) VALUES ({categoryId}, N'Spares');
            INSERT INTO [maintenance].[UnitsOfMeasure] ([Id], [UnitName], [Abbreviation]) VALUES ({unitId}, N'Piece', N'pcs');
            INSERT INTO [maintenance].[Suppliers] ([Id], [Name]) VALUES ({supplierId}, N'Existing supplier');
            INSERT INTO [maintenance].[InventoryItems]
                ([Id], [CategoryId], [UnitId], [SupplierId], [Name], [PartNumber], [Quantity], [MinimumQuantity], [UnitCost])
            VALUES ({itemId}, {categoryId}, {unitId}, {supplierId}, N'Main engine filter', N'PN-EXISTING', 7, 1, 25);
            INSERT INTO [maintenance].[PurchaseOrders] ([Id], [SupplierId], [OrderDate], [Status], [ConcurrencyToken])
            VALUES ({receivedOrderId}, {supplierId}, '2026-08-01', N'Received', NEWID()),
                   ({partialOrderId}, {supplierId}, '2026-08-02', N'PartiallyReceived', {partialConcurrencyToken}),
                   ({orderedOrderId}, {supplierId}, '2026-08-03', N'Ordered', NEWID()),
                   ({cancelledOrderId}, {supplierId}, '2026-08-04', N'Cancelled', NEWID());
            INSERT INTO [maintenance].[PurchaseOrderLines]
                ([Id], [PurchaseOrderId], [PartId], [QuantityOrdered], [UnitCost], [ReceivedQuantity], [LegacyReceivedQuantity])
            VALUES ({receivedLineId}, {receivedOrderId}, {itemId}, 10, 25, 10, 10),
                   ({partialLineId}, {partialOrderId}, {itemId}, 10, 25, 4, 0),
                   ({orderedLineId}, {orderedOrderId}, {itemId}, 4, 25, 0, 0),
                   ({cancelledLineId}, {cancelledOrderId}, {itemId}, 3, 25, 0, 0);
            INSERT INTO [maintenance].[PurchaseOrderReceipts]
                ([Id], [PurchaseOrderId], [ClientRequestId], [RequestHash], [ReceivedUtc], [RecordedByUserId], [RecordedUtc],
                 [OrderStatusBefore], [OrderStatusAfter])
            VALUES ({receiptId}, {partialOrderId}, {Guid.NewGuid()}, {requestHash}, {receivedUtc}, {ActorUserId}, {receivedUtc},
                    N'Ordered', N'PartiallyReceived');
            INSERT INTO [maintenance].[PurchaseOrderReceiptLines]
                ([Id], [PurchaseOrderReceiptId], [PurchaseOrderLineId], [InventoryItemId], [Quantity], [LineReceivedQuantityAfter])
            VALUES ({Guid.NewGuid()}, {receiptId}, {partialLineId}, {itemId}, 4, 4);
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
