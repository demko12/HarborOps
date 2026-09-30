using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PurchaseOrderReceiptDomainTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly string RequestHash = new('A', PurchaseOrderReceipt.RequestHashLength);

    [Fact]
    public void Create_records_the_delivery_the_recorder_and_both_order_statuses()
    {
        var (order, lines) = CreateOrderedOrder(10m);
        var clientRequestId = Guid.NewGuid();
        var receivedUtc = UtcNow.AddHours(-3);
        lines[0].Receive(4m);
        order.RecordReceipt(lines);

        var receipt = PurchaseOrderReceipt.Create(
            order,
            PurchaseOrderStatus.Ordered,
            clientRequestId,
            RequestHash,
            receivedUtc,
            "  DN-4471  ",
            "  Two boxes  ",
            ActorUserId,
            UtcNow);

        Assert.NotEqual(Guid.Empty, receipt.Id);
        Assert.Equal(order.Id, receipt.PurchaseOrderId);
        Assert.Equal(clientRequestId, receipt.ClientRequestId);
        Assert.Equal(RequestHash, receipt.RequestHash);
        Assert.Equal(receivedUtc, receipt.ReceivedUtc);
        Assert.Equal("DN-4471", receipt.DeliveryReference);
        Assert.Equal("Two boxes", receipt.Notes);
        Assert.Equal(ActorUserId, receipt.RecordedByUserId);
        Assert.Equal(UtcNow, receipt.RecordedUtc);
        Assert.Equal(PurchaseOrderStatus.Ordered, receipt.OrderStatusBefore);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, receipt.OrderStatusAfter);
    }

    [Fact]
    public void Create_turns_blank_reference_and_notes_into_null()
    {
        var receipt = CreateReceipt(deliveryReference: "   ", notes: "");

        Assert.Null(receipt.DeliveryReference);
        Assert.Null(receipt.Notes);
    }

    [Fact]
    public void Create_accepts_a_receipt_time_equal_to_now()
    {
        var receipt = CreateReceipt(receivedUtc: UtcNow);

        Assert.Equal(UtcNow, receipt.ReceivedUtc);
    }

    [Fact]
    public void Create_rejects_a_future_receipt_time()
    {
        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => CreateReceipt(receivedUtc: UtcNow.AddTicks(1)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Create_rejects_a_receipt_time_that_is_not_utc(DateTimeKind kind)
    {
        var receivedUtc = DateTime.SpecifyKind(UtcNow.AddHours(-1), kind);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => CreateReceipt(receivedUtc: receivedUtc));
    }

    [Fact]
    public void Create_requires_a_client_request_id()
    {
        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => CreateReceipt(clientRequestId: Guid.Empty));
    }

    [Fact]
    public void Create_rejects_a_malformed_request_fingerprint()
    {
        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => CreateReceipt(requestHash: "ABC"));
    }

    [Fact]
    public void Create_bounds_the_delivery_reference_and_notes()
    {
        var reference = new string('R', PurchaseOrderReceipt.DeliveryReferenceMaxLength);
        var notes = new string('N', PurchaseOrderReceipt.NotesMaxLength);

        var receipt = CreateReceipt(deliveryReference: reference, notes: notes);

        Assert.Equal(reference, receipt.DeliveryReference);
        Assert.Equal(notes, receipt.Notes);
        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => CreateReceipt(deliveryReference: reference + "R"));
        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => CreateReceipt(notes: notes + "N"));
    }

    [Fact]
    public void Line_records_the_quantity_and_the_cumulative_line_total()
    {
        var (order, lines) = CreateOrderedOrder(10m);
        lines[0].Receive(4m);
        lines[0].Receive(2.5m);
        order.RecordReceipt(lines);
        var receipt = CreateReceipt(order);

        var line = PurchaseOrderReceiptLine.Create(receipt, lines[0], 2.5m);

        Assert.NotEqual(Guid.Empty, line.Id);
        Assert.Equal(receipt.Id, line.PurchaseOrderReceiptId);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal(lines[0].PartId, line.InventoryItemId);
        Assert.Equal(2.5m, line.Quantity);
        Assert.Equal(6.5m, line.LineReceivedQuantityAfter);
    }

    [Fact]
    public void Line_rejects_a_purchase_order_line_from_another_order()
    {
        var receipt = CreateReceipt();
        var foreignLine = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 5m, 1m);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(
            () => PurchaseOrderReceiptLine.Create(receipt, foreignLine, 1m));
    }

    [Fact]
    public void Movement_records_the_increase_with_the_receipt_line_as_its_only_source()
    {
        var (order, lines) = CreateOrderedOrder(10m);
        lines[0].Receive(4m);
        order.RecordReceipt(lines);
        var receipt = CreateReceipt(order, deliveryReference: "DN-4471");
        var line = PurchaseOrderReceiptLine.Create(receipt, lines[0], 4m);

        var movement = InventoryAdjustment.CreateFromPurchaseOrderReceipt(receipt, line, 7.25m, ActorUserId, UtcNow);

        Assert.Equal(line.InventoryItemId, movement.InventoryItemId);
        Assert.Equal(receipt.Id, movement.PurchaseOrderReceiptId);
        Assert.Equal(line.Id, movement.PurchaseOrderReceiptLineId);
        Assert.Null(movement.StocktakeId);
        Assert.Null(movement.SupplierReturnId);
        Assert.Null(movement.SupplierReturnLineId);
        Assert.Equal(7.25m, movement.QuantityBefore);
        Assert.Equal(11.25m, movement.QuantityAfter);
        Assert.Equal(4m, movement.QuantityDelta);
        Assert.Equal("Received from purchase order, delivery DN-4471", movement.Reason);
        Assert.Equal(ActorUserId, movement.ActorUserId);
        Assert.Equal(UtcNow, movement.AdjustedUtc);
    }

    [Fact]
    public void Movement_reason_omits_a_missing_delivery_reference()
    {
        Assert.Equal("Received from purchase order", CreateReceipt().MovementReason());
    }

    [Fact]
    public void Movement_rejects_a_line_of_another_receipt()
    {
        var (order, lines) = CreateOrderedOrder(10m);
        lines[0].Receive(4m);
        order.RecordReceipt(lines);
        var receipt = CreateReceipt(order);
        var otherLine = PurchaseOrderReceiptLine.Create(CreateReceipt(order), lines[0], 4m);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(
            () => InventoryAdjustment.CreateFromPurchaseOrderReceipt(receipt, otherLine, 0m, ActorUserId, UtcNow));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("1000000000000000")]
    public void Line_rejects_zero_negative_over_precise_and_oversized_quantities(string quantity)
    {
        var (order, lines) = CreateOrderedOrder(10m);
        lines[0].Receive(1m);
        order.RecordReceipt(lines);
        var receipt = CreateReceipt(order);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(
            () => PurchaseOrderReceiptLine.Create(receipt, lines[0], decimal.Parse(quantity)));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1.001", true)]
    [InlineData("1.0001", false)]
    public void HasSupportedPrecision_allows_three_decimal_places(string quantity, bool expected)
    {
        Assert.Equal(expected, PurchaseOrderReceiptLine.HasSupportedPrecision(decimal.Parse(quantity)));
    }

    private static PurchaseOrderReceipt CreateReceipt(
        PurchaseOrder? order = null,
        Guid? clientRequestId = null,
        string? requestHash = null,
        DateTime? receivedUtc = null,
        string? deliveryReference = null,
        string? notes = null)
    {
        if (order is null)
        {
            var (ordered, lines) = CreateOrderedOrder(10m);
            lines[0].Receive(1m);
            ordered.RecordReceipt(lines);
            order = ordered;
        }

        return PurchaseOrderReceipt.Create(
            order,
            PurchaseOrderStatus.Ordered,
            clientRequestId ?? Guid.NewGuid(),
            requestHash ?? RequestHash,
            receivedUtc ?? UtcNow.AddHours(-1),
            deliveryReference,
            notes,
            ActorUserId,
            UtcNow);
    }

    private static (PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines) CreateOrderedOrder(
        params decimal[] quantities)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), new DateOnly(2026, 8, 1), null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

        var lines = quantities
            .Select(quantity => PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), quantity, 1m))
            .ToList();

        return (order, lines);
    }
}
