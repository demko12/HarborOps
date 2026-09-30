using System.Data.Common;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Verifies the receipt tables, their unique indexes, and the all-or-nothing commit on SQL Server.
/// </summary>
public sealed class PurchaseOrderReceiptPersistenceRelationalTests : IAsyncLifetime
{
    private const int UniqueIndexViolation = 2601;

    private static readonly Guid ActorUserId =
        Guid.Parse("0b1c2d3e-4f50-4617-a8b9-cadbecfd0e1f");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
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
    public async Task A_posted_receipt_round_trips_with_its_evidence_and_stock_movement()
    {
        var (itemId, order) = await SeedOrderedOrderAsync(quantityOnHand: 2m, ordered: 10m);
        var receivedUtc = new DateTime(2026, 9, 27, 13, 45, 12, DateTimeKind.Utc).AddTicks(1234567);
        var request = await RequestAsync(order, 4.125m) with
        {
            ReceivedUtc = receivedUtc,
            DeliveryReference = "DN-4471",
            Notes = "Pallet 1 of 2",
        };

        var response = await PostAsync(order.Id, request);

        await using var dbContext = CreateDbContext();
        var receipt = await dbContext.Set<PurchaseOrderReceipt>().AsNoTracking().SingleAsync();
        var line = await dbContext.Set<PurchaseOrderReceiptLine>().AsNoTracking().SingleAsync();
        var movement = await dbContext.Set<InventoryAdjustment>().AsNoTracking().SingleAsync();
        var storedOrder = await dbContext.Set<PurchaseOrder>().AsNoTracking().SingleAsync();

        Assert.Equal(response.Receipt.ReceiptId, receipt.Id);
        Assert.Equal(request.ClientRequestId, receipt.ClientRequestId);
        Assert.Equal(receivedUtc.Ticks, receipt.ReceivedUtc.Ticks);
        Assert.Equal("DN-4471", receipt.DeliveryReference);
        Assert.Equal("Pallet 1 of 2", receipt.Notes);
        Assert.Equal(ActorUserId, receipt.RecordedByUserId);
        Assert.Equal(PurchaseOrderReceipt.RequestHashLength, receipt.RequestHash.Length);
        Assert.Equal(PurchaseOrderStatus.Ordered, receipt.OrderStatusBefore);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, receipt.OrderStatusAfter);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, storedOrder.Status);
        Assert.Equal(4.125m, line.Quantity);
        Assert.Equal(4.125m, line.LineReceivedQuantityAfter);
        Assert.Equal(itemId, line.InventoryItemId);
        Assert.Equal(receipt.Id, movement.PurchaseOrderReceiptId);
        Assert.Equal(line.Id, movement.PurchaseOrderReceiptLineId);
        Assert.Null(movement.StocktakeId);
        Assert.Null(movement.SupplierReturnId);
        Assert.Equal(2m, movement.QuantityBefore);
        Assert.Equal(6.125m, movement.QuantityAfter);
        Assert.Equal(4.125m, movement.QuantityDelta);
        Assert.Equal("Received from purchase order, delivery DN-4471", movement.Reason);
    }

    [RelationalFact]
    public async Task The_database_rejects_a_second_receipt_with_the_same_client_request_id()
    {
        var (_, order) = await SeedOrderedOrderAsync(quantityOnHand: 0m, ordered: 10m);
        var response = await PostAsync(order.Id, await RequestAsync(order, 1m));

        await using var dbContext = CreateDbContext();
        var purchaseOrder = await dbContext.Set<PurchaseOrder>().AsNoTracking().SingleAsync();
        await dbContext.Set<PurchaseOrderReceipt>().AddAsync(PurchaseOrderReceipt.Create(
            purchaseOrder,
            purchaseOrder.Status,
            response.Receipt.ClientRequestId,
            new string('0', PurchaseOrderReceipt.RequestHashLength),
            DateTime.UtcNow.AddHours(-1),
            null,
            null,
            ActorUserId,
            DateTime.UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [RelationalFact]
    public async Task The_database_rejects_a_receipt_listing_one_order_line_twice()
    {
        var (_, order) = await SeedOrderedOrderAsync(quantityOnHand: 0m, ordered: 10m);
        var response = await PostAsync(order.Id, await RequestAsync(order, 1m));

        await using var dbContext = CreateDbContext();
        var receipt = await dbContext.Set<PurchaseOrderReceipt>().AsNoTracking().SingleAsync();
        var orderLine = await dbContext.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync();
        await dbContext.Set<PurchaseOrderReceiptLine>().AddAsync(PurchaseOrderReceiptLine.Create(receipt, orderLine, 1m));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(response.Receipt.ReceiptId, receipt.Id);
        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [RelationalFact]
    public async Task The_database_rejects_a_second_stock_movement_for_one_receipt_line()
    {
        var (_, order) = await SeedOrderedOrderAsync(quantityOnHand: 0m, ordered: 10m);
        await PostAsync(order.Id, await RequestAsync(order, 1m));

        await using var dbContext = CreateDbContext();
        var receipt = await dbContext.Set<PurchaseOrderReceipt>().AsNoTracking().SingleAsync();
        var line = await dbContext.Set<PurchaseOrderReceiptLine>().AsNoTracking().SingleAsync();
        await dbContext.Set<InventoryAdjustment>().AddAsync(
            InventoryAdjustment.CreateFromPurchaseOrderReceipt(receipt, line, 1m, ActorUserId, DateTime.UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(UniqueIndexViolation, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [RelationalFact]
    public async Task A_failed_commit_leaves_no_receipt_movement_stock_line_total_or_status_behind()
    {
        var (itemId, order) = await SeedOrderedOrderAsync(quantityOnHand: 3m, ordered: 10m);
        var request = await RequestAsync(order, 4m);
        var failingCommit = new FailingCommitInterceptor();

        await using (var dbContext = CreateDbContext(failingCommit))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
                    .PostAsync(order.Id, request, ActorUserId, CancellationToken.None));
        }

        Assert.True(failingCommit.Failed);

        await using (var verification = CreateDbContext())
        {
            Assert.Empty(await verification.Set<PurchaseOrderReceipt>().AsNoTracking().ToListAsync());
            Assert.Empty(await verification.Set<PurchaseOrderReceiptLine>().AsNoTracking().ToListAsync());
            Assert.Empty(await verification.Set<InventoryAdjustment>().AsNoTracking().ToListAsync());
            Assert.Equal(3m, (await verification.Set<InventoryItem>().AsNoTracking().SingleAsync(item => item.Id == itemId)).Quantity);
            Assert.Equal(0m, (await verification.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync()).ReceivedQuantity);

            var stored = await verification.Set<PurchaseOrder>().AsNoTracking().SingleAsync();
            Assert.Equal(PurchaseOrderStatus.Ordered, stored.Status);
            Assert.Equal(order.ConcurrencyToken, stored.ConcurrencyToken);
        }

        // Nothing changed, so the identical confirmation can simply be sent again and is posted exactly once.
        var retried = await PostAsync(order.Id, request);

        Assert.False(retried.AlreadyPosted);

        await using var afterRetry = CreateDbContext();
        Assert.Equal(7m, (await afterRetry.Set<InventoryItem>().AsNoTracking().SingleAsync(item => item.Id == itemId)).Quantity);
        Assert.Single(await afterRetry.Set<PurchaseOrderReceipt>().AsNoTracking().ToListAsync());
        Assert.Single(await afterRetry.Set<InventoryAdjustment>().AsNoTracking().ToListAsync());
    }

    [RelationalFact]
    public async Task A_stale_purchase_order_token_is_rejected_by_the_database()
    {
        var (_, order) = await SeedOrderedOrderAsync(quantityOnHand: 0m, ordered: 10m);

        await using var staleContext = CreateDbContext();
        var stale = await staleContext.Set<PurchaseOrder>().SingleAsync(candidate => candidate.Id == order.Id);

        await PostAsync(order.Id, await RequestAsync(order, 1m));

        stale.ChangeStatusTo(PurchaseOrderStatus.Cancelled);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
    }

    private sealed record OrderSeed(Guid Id, Guid ConcurrencyToken, Guid LineId);

    // Previews the receipt and returns the request a user would send to confirm exactly that preview.
    private async Task<PostPurchaseOrderReceiptRequest> RequestAsync(OrderSeed order, decimal quantity)
    {
        var receivedUtc = DateTime.UtcNow.AddHours(-1);
        var lines = new[] { new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = order.LineId, Quantity = quantity } };

        await using var dbContext = CreateDbContext();

        var preview = await new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, TimeProvider.System).GetPreviewAsync(
            order.Id,
            new PreviewPurchaseOrderReceiptRequest { ReceivedUtc = receivedUtc, Lines = lines },
            CancellationToken.None);

        return new PostPurchaseOrderReceiptRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = preview.ConcurrencyToken,
            PreviewToken = preview.PreviewToken,
            ReceivedUtc = receivedUtc,
            Lines = lines,
        };
    }

    private async Task<PostPurchaseOrderReceiptResponse> PostAsync(Guid purchaseOrderId, PostPurchaseOrderReceiptRequest request)
    {
        await using var dbContext = CreateDbContext();

        return await new PostPurchaseOrderReceiptService(dbContext, TimeProvider.System)
            .PostAsync(purchaseOrderId, request, ActorUserId, CancellationToken.None);
    }

    private async Task<(Guid ItemId, OrderSeed Order)> SeedOrderedOrderAsync(decimal quantityOnHand, decimal ordered)
    {
        await using var dbContext = CreateDbContext();

        var category = ItemCategory.Create($"Spares {Guid.NewGuid():N}", null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var supplier = Supplier.Register($"Supplier {Guid.NewGuid():N}", null, null, null, null);
        var item = InventoryItem.Create(
            category.Id,
            unit.Id,
            supplier.Id,
            "Main engine filter",
            $"PN-{Guid.NewGuid():N}",
            quantityOnHand,
            minimumQuantity: 1m,
            unitCost: 25m);
        var order = PurchaseOrder.Create(supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        var line = PurchaseOrderLine.Create(order.Id, item.Id, ordered, 25m);

        await dbContext.AddRangeAsync(category, unit, supplier, item, order, line);
        await dbContext.SaveChangesAsync();

        return (item.Id, new OrderSeed(order.Id, order.ConcurrencyToken, line.Id));
    }

    private HarborOpsDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .AddInterceptors(interceptors)
            .Options;

        return new HarborOpsDbContext(options);
    }

    // Fails the first commit after every statement of the receipt has already run inside the transaction.
    private sealed class FailingCommitInterceptor : DbTransactionInterceptor
    {
        internal bool Failed { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Failed)
            {
                Failed = true;
                throw new InvalidOperationException("Simulated failure while committing the receipt.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
