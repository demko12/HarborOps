using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PurchaseOrderClosureDomainTests
{
    private const string Reason = "Supplier discontinued the part";

    private static readonly DateOnly Today = new(2026, 8, 20);
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly string RequestHash = new('A', PurchaseOrderClosure.RequestHashLength);

    [Fact]
    public void CloseOutstanding_closes_the_remaining_quantity_without_touching_ordered_or_received()
    {
        var (_, lines) = CreatePartiallyReceivedOrder((10m, 4m));

        var closed = lines[0].CloseOutstanding();

        Assert.Equal(6m, closed);
        Assert.Equal(6m, lines[0].ClosedQuantity);
        Assert.Equal(10m, lines[0].QuantityOrdered);
        Assert.Equal(4m, lines[0].ReceivedQuantity);
        Assert.Equal(0m, lines[0].OutstandingQuantity);
    }

    [Fact]
    public void CloseOutstanding_closes_nothing_on_a_fully_received_line()
    {
        var (_, lines) = CreatePartiallyReceivedOrder((10m, 4m), (2m, 2m));

        Assert.Equal(0m, lines[1].CloseOutstanding());
        Assert.Equal(0m, lines[1].ClosedQuantity);
        Assert.Equal(0m, lines[1].OutstandingQuantity);
    }

    [Fact]
    public void CloseOutstanding_rejects_a_line_that_was_already_closed()
    {
        var (_, lines) = CreatePartiallyReceivedOrder((10m, 4m));
        lines[0].CloseOutstanding();

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => lines[0].CloseOutstanding());

        Assert.Equal(6m, lines[0].ClosedQuantity);
    }

    [Fact]
    public void Receive_rejects_any_quantity_once_the_line_is_closed()
    {
        var (_, lines) = CreatePartiallyReceivedOrder((10m, 4m));
        lines[0].CloseOutstanding();

        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => lines[0].Receive(0.001m));

        Assert.Equal(4m, lines[0].ReceivedQuantity);
    }

    [Fact]
    public void RecordClosure_makes_the_order_closed_short_and_rotates_the_token()
    {
        var (order, lines) = CreatePartiallyReceivedOrder((10m, 4m), (3m, 0m));
        var token = order.ConcurrencyToken;

        foreach (var line in lines)
        {
            line.CloseOutstanding();
        }

        order.RecordClosure(lines);

        Assert.Equal(PurchaseOrderStatus.ClosedShort, order.Status);
        Assert.NotEqual(token, order.ConcurrencyToken);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    [InlineData(PurchaseOrderStatus.ClosedShort)]
    public void RecordClosure_rejects_orders_that_cannot_be_closed(PurchaseOrderStatus status)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        var line = PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 5m, 1m);
        PurchaseOrderTestData.ApplyStatus(order, status, [line]);
        var token = order.ConcurrencyToken;

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => order.RecordClosure([line]));

        Assert.Equal(status, order.Status);
        Assert.Equal(token, order.ConcurrencyToken);
    }

    [Fact]
    public void RecordClosure_rejects_a_line_that_still_has_outstanding_quantity()
    {
        var (order, lines) = CreatePartiallyReceivedOrder((10m, 4m), (3m, 0m));
        lines[0].CloseOutstanding();

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => order.RecordClosure(lines));

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
    }

    [Fact]
    public void RecordClosure_rejects_lines_of_another_order()
    {
        var (order, _) = CreatePartiallyReceivedOrder((10m, 4m));
        var foreignLine = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 1m, 1m);

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => order.RecordClosure([foreignLine]));
        Assert.Throws<PurchaseOrderClosureInvariantException>(() => order.RecordClosure([]));
    }

    [Fact]
    public void Create_records_the_reason_the_actor_and_both_order_statuses()
    {
        var clientRequestId = Guid.NewGuid();
        var (order, _) = CreateClosedOrder();

        var closure = PurchaseOrderClosure.Create(
            order,
            PurchaseOrderStatus.PartiallyReceived,
            clientRequestId,
            RequestHash,
            $"  {Reason}  ",
            ActorUserId,
            UtcNow);

        Assert.NotEqual(Guid.Empty, closure.Id);
        Assert.Equal(order.Id, closure.PurchaseOrderId);
        Assert.Equal(clientRequestId, closure.ClientRequestId);
        Assert.Equal(RequestHash, closure.RequestHash);
        Assert.Equal(Reason, closure.Reason);
        Assert.Equal(ActorUserId, closure.ClosedByUserId);
        Assert.Equal(UtcNow, closure.ClosedUtc);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, closure.OrderStatusBefore);
        Assert.Equal(PurchaseOrderStatus.ClosedShort, closure.OrderStatusAfter);
    }

    [Fact]
    public void Create_requires_the_order_to_be_closed_first()
    {
        var (order, _) = CreatePartiallyReceivedOrder((10m, 4m));

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => CreateClosure(order));
    }

    [Fact]
    public void Create_requires_a_client_request_id_a_valid_fingerprint_and_an_actor()
    {
        var (order, _) = CreateClosedOrder();

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => CreateClosure(order, clientRequestId: Guid.Empty));
        Assert.Throws<PurchaseOrderClosureInvariantException>(() => CreateClosure(order, requestHash: "ABC"));
        Assert.Throws<PurchaseOrderClosureInvariantException>(() => CreateClosure(order, actorUserId: Guid.Empty));
    }

    [Theory]
    [InlineData(PurchaseOrderClosure.ReasonMinLength - 1)]
    [InlineData(PurchaseOrderClosure.ReasonMaxLength + 1)]
    public void Create_rejects_a_reason_outside_the_allowed_length(int length)
    {
        var (order, _) = CreateClosedOrder();

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => CreateClosure(order, reason: new string('R', length)));
    }

    [Fact]
    public void Create_measures_the_reason_after_trimming()
    {
        var (order, _) = CreateClosedOrder();
        var padded = $"   {new string('R', PurchaseOrderClosure.ReasonMinLength - 1)}   ";

        Assert.Throws<PurchaseOrderClosureInvariantException>(() => CreateClosure(order, reason: padded));
    }

    [Fact]
    public void ClosureLine_snapshots_ordered_received_returned_and_closed_quantities()
    {
        var (order, lines) = CreateClosedOrder((10m, 4m));
        var closure = CreateClosure(order);

        var line = PurchaseOrderClosureLine.Create(closure, lines[0], returnedQuantity: 1m);

        Assert.NotEqual(Guid.Empty, line.Id);
        Assert.Equal(closure.Id, line.PurchaseOrderClosureId);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal(lines[0].PartId, line.InventoryItemId);
        Assert.Equal(10m, line.QuantityOrdered);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ClosedQuantity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4.001)]
    public void ClosureLine_rejects_a_returned_quantity_outside_what_was_received(decimal returnedQuantity)
    {
        var (order, lines) = CreateClosedOrder((10m, 4m));
        var closure = CreateClosure(order);

        Assert.Throws<PurchaseOrderClosureInvariantException>(
            () => PurchaseOrderClosureLine.Create(closure, lines[0], returnedQuantity));
    }

    [Fact]
    public void ClosureLine_rejects_a_line_of_another_order()
    {
        var (order, _) = CreateClosedOrder();
        var (_, otherLines) = CreateClosedOrder();
        var closure = CreateClosure(order);

        Assert.Throws<PurchaseOrderClosureInvariantException>(
            () => PurchaseOrderClosureLine.Create(closure, otherLines[0], 0m));
    }

    private static PurchaseOrderClosure CreateClosure(
        PurchaseOrder order,
        Guid? clientRequestId = null,
        string? requestHash = null,
        string reason = Reason,
        Guid? actorUserId = null)
    {
        return PurchaseOrderClosure.Create(
            order,
            PurchaseOrderStatus.PartiallyReceived,
            clientRequestId ?? Guid.NewGuid(),
            requestHash ?? RequestHash,
            reason,
            actorUserId ?? ActorUserId,
            UtcNow);
    }

    private static (PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines) CreateClosedOrder(
        params (decimal Ordered, decimal Received)[] quantities)
    {
        var (order, lines) = CreatePartiallyReceivedOrder(quantities.Length == 0 ? [(10m, 4m)] : quantities);

        PurchaseOrderTestData.CloseShort(order, lines);

        return (order, lines);
    }

    private static (PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines) CreatePartiallyReceivedOrder(
        params (decimal Ordered, decimal Received)[] quantities)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

        var lines = quantities
            .Select(quantity => PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), quantity.Ordered, 1m))
            .ToList();

        PurchaseOrderTestData.ReceivePartially(
            order,
            lines,
            lines
                .Select((line, index) => (line.Id, quantities[index].Received))
                .Where(entry => entry.Received > 0)
                .ToDictionary(entry => entry.Id, entry => entry.Received));

        return (order, lines);
    }
}
