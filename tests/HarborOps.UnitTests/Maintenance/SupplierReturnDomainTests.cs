using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class SupplierReturnDomainTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public void Create_starts_a_draft_for_the_order_supplier_with_audit_fields()
    {
        var order = CreateOrder(PurchaseOrderStatus.Received);

        var supplierReturn = SupplierReturn.Create(order, "  Wrong spec  ", ActorUserId, UtcNow);

        Assert.Equal(SupplierReturnStatus.Draft, supplierReturn.Status);
        Assert.Equal(order.Id, supplierReturn.PurchaseOrderId);
        Assert.Equal(order.SupplierId, supplierReturn.SupplierId);
        Assert.Equal("Wrong spec", supplierReturn.Notes);
        Assert.Equal(ActorUserId, supplierReturn.CreatedByUserId);
        Assert.Equal(UtcNow, supplierReturn.CreatedUtc);
        Assert.NotEqual(Guid.Empty, supplierReturn.ConcurrencyToken);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void Create_rejects_an_order_that_was_not_received(PurchaseOrderStatus status)
    {
        var order = CreateOrder(status);

        Assert.Throws<SupplierReturnInvariantException>(() =>
            SupplierReturn.Create(order, null, ActorUserId, UtcNow));
    }

    [Fact]
    public void Create_accepts_a_partially_received_order()
    {
        var order = CreateOrder(PurchaseOrderStatus.PartiallyReceived);

        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);

        Assert.Equal(SupplierReturnStatus.Draft, supplierReturn.Status);
        Assert.Equal(order.Id, supplierReturn.PurchaseOrderId);
    }

    [Fact]
    public void Line_copies_the_part_from_the_purchase_order_line_and_trims_the_reason()
    {
        var order = CreateOrder(PurchaseOrderStatus.Received);
        var orderLine = PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 10m, 5m);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);

        var line = SupplierReturnLine.Create(supplierReturn, orderLine, 2.125m, "  Damaged  ");

        Assert.Equal(supplierReturn.Id, line.SupplierReturnId);
        Assert.Equal(orderLine.Id, line.PurchaseOrderLineId);
        Assert.Equal(orderLine.PartId, line.InventoryItemId);
        Assert.Equal(2.125m, line.Quantity);
        Assert.Equal("Damaged", line.Reason);
    }

    [Fact]
    public void Line_rejects_a_purchase_order_line_from_another_order()
    {
        var order = CreateOrder(PurchaseOrderStatus.Received);
        var foreignLine = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, 5m);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);

        Assert.Throws<SupplierReturnInvariantException>(() =>
            SupplierReturnLine.Create(supplierReturn, foreignLine, 1m, "Damaged"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("1.2345")]
    public void Line_rejects_zero_negative_and_over_precise_quantities(string quantity)
    {
        var line = CreateLine();

        Assert.Throws<SupplierReturnInvariantException>(() =>
            line.Change(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), "Damaged"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Line_requires_a_reason(string reason)
    {
        var line = CreateLine();

        Assert.Throws<SupplierReturnInvariantException>(() => line.Change(1m, reason));
    }

    [Fact]
    public void Line_rejects_a_reason_that_is_too_long()
    {
        var line = CreateLine();

        Assert.Throws<SupplierReturnInvariantException>(() =>
            line.Change(1m, new string('x', SupplierReturn.ReasonMaxLength + 1)));
    }

    [Fact]
    public void RecordChange_rotates_the_token_and_records_the_editor()
    {
        var supplierReturn = SupplierReturn.Create(CreateOrder(PurchaseOrderStatus.Received), null, ActorUserId, UtcNow);
        var token = supplierReturn.ConcurrencyToken;

        supplierReturn.ChangeNotes("Updated", ActorUserId, UtcNow.AddMinutes(5));

        Assert.NotEqual(token, supplierReturn.ConcurrencyToken);
        Assert.Equal("Updated", supplierReturn.Notes);
        Assert.Equal(ActorUserId, supplierReturn.LastModifiedByUserId);
        Assert.Equal(UtcNow.AddMinutes(5), supplierReturn.LastModifiedUtc);
    }

    [Fact]
    public void MarkPosted_records_the_poster_and_makes_the_return_immutable()
    {
        var order = CreateOrder(PurchaseOrderStatus.Received);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);
        var line = SupplierReturnLine.Create(
            supplierReturn,
            PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 10m, 5m),
            1m,
            "Damaged");
        var token = supplierReturn.ConcurrencyToken;

        supplierReturn.MarkPosted([line], ActorUserId, UtcNow);

        Assert.Equal(SupplierReturnStatus.Posted, supplierReturn.Status);
        Assert.Equal(ActorUserId, supplierReturn.PostedByUserId);
        Assert.Equal(UtcNow, supplierReturn.PostedUtc);
        Assert.NotEqual(token, supplierReturn.ConcurrencyToken);
        Assert.Throws<SupplierReturnNotDraftException>(() => supplierReturn.ChangeNotes("Late", ActorUserId, UtcNow));
        Assert.Throws<SupplierReturnNotDraftException>(() => supplierReturn.Cancel("Late", ActorUserId, UtcNow));
        Assert.Throws<SupplierReturnNotDraftException>(() => supplierReturn.MarkPosted([line], ActorUserId, UtcNow));
    }

    [Fact]
    public void MarkPosted_requires_at_least_one_line()
    {
        var supplierReturn = SupplierReturn.Create(CreateOrder(PurchaseOrderStatus.Received), null, ActorUserId, UtcNow);

        Assert.Throws<SupplierReturnInvariantException>(() => supplierReturn.MarkPosted([], ActorUserId, UtcNow));
    }

    [Fact]
    public void Cancel_records_the_reason_and_blocks_further_changes()
    {
        var supplierReturn = SupplierReturn.Create(CreateOrder(PurchaseOrderStatus.Received), null, ActorUserId, UtcNow);

        supplierReturn.Cancel("  Supplier refused  ", ActorUserId, UtcNow);

        Assert.Equal(SupplierReturnStatus.Cancelled, supplierReturn.Status);
        Assert.Equal("Supplier refused", supplierReturn.CancellationReason);
        Assert.Equal(ActorUserId, supplierReturn.CancelledByUserId);
        Assert.Equal(UtcNow, supplierReturn.CancelledUtc);
        Assert.Throws<SupplierReturnNotDraftException>(() => supplierReturn.RecordChange(ActorUserId, UtcNow));
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var supplierReturn = SupplierReturn.Create(CreateOrder(PurchaseOrderStatus.Received), null, ActorUserId, UtcNow);

        Assert.Throws<SupplierReturnInvariantException>(() => supplierReturn.Cancel(" ", ActorUserId, UtcNow));
    }

    [Fact]
    public void Movement_records_before_and_after_quantities_with_the_source_return_line()
    {
        var line = CreateLine();

        var movement = InventoryAdjustment.CreateFromSupplierReturn(line, 10m, ActorUserId, UtcNow);

        Assert.Equal(line.InventoryItemId, movement.InventoryItemId);
        Assert.Null(movement.StocktakeId);
        Assert.Equal(line.SupplierReturnId, movement.SupplierReturnId);
        Assert.Equal(line.Id, movement.SupplierReturnLineId);
        Assert.Equal(10m, movement.QuantityBefore);
        Assert.Equal(7m, movement.QuantityAfter);
        Assert.Equal(-3m, movement.QuantityDelta);
        Assert.Equal(line.Reason, movement.Reason);
        Assert.Equal(ActorUserId, movement.ActorUserId);
        Assert.Equal(UtcNow, movement.AdjustedUtc);
    }

    [Fact]
    public void Movement_cannot_make_stock_negative()
    {
        var line = CreateLine();

        Assert.Throws<SupplierReturnInvariantException>(() =>
            InventoryAdjustment.CreateFromSupplierReturn(line, 2.999m, ActorUserId, UtcNow));
    }

    [Fact]
    public void Stocktake_adjustments_keep_their_counted_quantity_as_the_after_quantity()
    {
        var adjustment = InventoryAdjustment.CreateFromStocktake(
            Guid.NewGuid(),
            Guid.NewGuid(),
            10m,
            8m,
            "Missing",
            ActorUserId,
            UtcNow);

        Assert.Null(adjustment.SupplierReturnId);
        Assert.Null(adjustment.SupplierReturnLineId);
        Assert.Equal(8m, adjustment.QuantityAfter);
        Assert.Equal(-2m, adjustment.QuantityDelta);
    }

    private static SupplierReturnLine CreateLine()
    {
        var order = CreateOrder(PurchaseOrderStatus.Received);
        var supplierReturn = SupplierReturn.Create(order, null, ActorUserId, UtcNow);

        return SupplierReturnLine.Create(
            supplierReturn,
            PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 10m, 5m),
            3m,
            "Damaged");
    }

    private static PurchaseOrder CreateOrder(PurchaseOrderStatus status)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), new DateOnly(2026, 8, 1), null);

        PurchaseOrderTestData.ApplyStatus(
            order,
            status,
            [PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 10m, 5m)]);

        return order;
    }
}
